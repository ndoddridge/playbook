using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Writes immutable-per-run research artifacts under research-runs/{runId}/.
/// Refuses to overwrite an existing run directory.
/// </summary>
public sealed class ResearchRunStore
{
    private readonly string _root;

    public ResearchRunStore(string? rootDirectory = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? ResearchIntegrity.DefaultOutputRoot);
    }

    public string Root => _root;

    public string CreateRunDirectory(string commandLabel, DateTimeOffset timestampUtc)
    {
        Directory.CreateDirectory(_root);
        var stamp = timestampUtc.ToString("yyyyMMdd-HHmmss");
        var runId = $"{stamp}-{Sanitize(commandLabel)}-{Guid.NewGuid().ToString("N")[..8]}";
        var path = Path.Combine(_root, runId);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException($"Run directory already exists: {path}");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public void WriteText(string runDirectory, string fileName, string contents)
    {
        var path = Path.Combine(runDirectory, fileName);
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Refusing to overwrite research artifact: {path}");
        }

        File.WriteAllText(path, contents, Encoding.UTF8);
    }

    public void WriteJson<T>(string runDirectory, string fileName, T value)
    {
        WriteText(runDirectory, fileName, JsonSerializer.Serialize(value, ResearchJson.Indented));
    }

    public ResearchRunManifest WriteManifest(string runDirectory, ResearchRunManifest manifest)
    {
        WriteText(runDirectory, "manifest.json", manifest.ToJson());
        return manifest;
    }

    public static ResearchRunManifest LoadManifest(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing manifest.json in {runDirectory}");
        }

        return JsonSerializer.Deserialize<ResearchRunManifest>(
                   File.ReadAllText(path), ResearchJson.Indented)
               ?? throw new InvalidOperationException($"Invalid manifest: {path}");
    }

    public static ResearchRunMetrics? TryLoadMetrics(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "metrics.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ResearchRunMetrics>(
            File.ReadAllText(path), ResearchJson.Indented);
    }

    public static (string? Sha, string? Branch) CaptureGitIdentity(string workingDirectory)
    {
        string? sha = null;
        string? branch = null;
        try
        {
            sha = RunGit(workingDirectory, "rev-parse HEAD");
            branch = RunGit(workingDirectory, "branch --show-current");
        }
        catch
        {
            // Git may be unavailable in some environments; leave null.
        }

        return (sha, branch);
    }

    private static string? RunGit(string workingDirectory, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            return null;
        }

        var stdout = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(5000);
        return p.ExitCode == 0 && stdout.Length > 0 ? stdout : null;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal))
        {
            s = s.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(s) ? "run" : s.Trim('-');
    }
}
