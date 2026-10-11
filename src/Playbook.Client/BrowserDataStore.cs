using Microsoft.JSInterop;

namespace Playbook.Client;

/// <summary>
/// Playbook keeps its caches and saved state (connected leagues, imported drafts, personal draft
/// learning) as files under a data directory. In the browser that directory is in memory, so this
/// copies it to IndexedDB: everything saved is put back before the app starts, and changes are
/// written out every few seconds. If IndexedDB is unavailable (a private window, blocked storage)
/// the app still runs; it just starts fresh on each visit.
/// </summary>
public sealed class BrowserDataStore : IAsyncDisposable
{
    public static readonly string Root = Path.Combine(AppContext.BaseDirectory, "data");

    private readonly IJSRuntime _js;
    private readonly Dictionary<string, (long Length, DateTime Written)> _saved = new(StringComparer.Ordinal);
    private IJSObjectReference? _module;
    private PeriodicTimer? _timer;
    private bool _available = true;

    public BrowserDataStore(IJSRuntime js)
    {
        _js = js;
    }

    public async Task RestoreAsync()
    {
        Directory.CreateDirectory(Root);
        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/data-store.js");
            var keys = await _module.InvokeAsync<string[]>("keys");
            foreach (var key in keys)
            {
                var path = PathFor(key);
                if (path is null)
                {
                    continue;
                }

                var bytes = await _module.InvokeAsync<byte[]>("read", key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
                var info = new FileInfo(path);
                _saved[key] = (info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or IOException)
        {
            _available = false;
            Console.WriteLine($"Playbook: browser storage unavailable, starting fresh ({ex.Message}).");
        }
    }

    public void StartSaving(TimeSpan interval)
    {
        if (!_available || _module is null || _timer is not null)
        {
            return;
        }

        _timer = new PeriodicTimer(interval);
        _ = SaveLoopAsync(_timer);
    }

    private async Task SaveLoopAsync(PeriodicTimer timer)
    {
        while (await timer.WaitForNextTickAsync())
        {
            await SaveChangesAsync();
        }
    }

    public async Task SaveChangesAsync()
    {
        if (!_available || _module is null)
        {
            return;
        }

        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                var key = Path.GetRelativePath(Root, file).Replace('\\', '/');
                seen.Add(key);
                if (_saved.TryGetValue(key, out var prior) &&
                    prior.Length == info.Length && prior.Written == info.LastWriteTimeUtc)
                {
                    continue;
                }

                await _module.InvokeVoidAsync("write", key, await File.ReadAllBytesAsync(file));
                _saved[key] = (info.Length, info.LastWriteTimeUtc);
            }

            foreach (var gone in _saved.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                await _module.InvokeVoidAsync("remove", gone);
                _saved.Remove(gone);
            }
        }
        catch (Exception ex) when (ex is JSException or IOException)
        {
            Console.WriteLine($"Playbook: could not save to browser storage ({ex.Message}).");
        }
    }

    /// <summary>A stored key as a path under <see cref="Root"/>, or null if it would leave it.</summary>
    public static string? PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(Root, key));
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
