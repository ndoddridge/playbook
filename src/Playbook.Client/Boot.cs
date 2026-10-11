using System.Diagnostics;
using Microsoft.JSInterop;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;

namespace Playbook.Client;

/// <summary>
/// The server loads data the first time a page asks for it, blocking that request. A browser
/// cannot block on the network, so the data the pages read synchronously is loaded here, before
/// the first render, while index.html shows what is loading.
/// </summary>
public static class Boot
{
    public static async Task WarmUpAsync(IServiceProvider services)
    {
        var players = services.GetRequiredService<IPlayerService>();
        var calendar = services.GetRequiredService<INflCalendarService>();
        await StepAsync(services, "Loading players", () => Task.WhenAll(
            Quietly(() => players.RefreshAsync()),
            Quietly(() => calendar.RefreshAsync())));

        var stats = services.GetRequiredService<IPlayerStatsService>();
        var injuries = services.GetRequiredService<IPlayerInjuryService>();
        var news = services.GetRequiredService<INewsProvider>();
        await StepAsync(services, "Loading stats, injuries and news", () => Task.WhenAll(
            Quietly(() => stats.EnsureLoadedAsync()),
            Quietly(() => injuries.EnsureLoadedAsync()),
            Quietly(() => news.RefreshAsync())));

        await StepAsync(services, "Analyzing", () =>
        {
            services.GetRequiredService<IIntelligenceService>().Refresh();
            services.GetRequiredService<IProjectionService>().Refresh();
            return Task.CompletedTask;
        });
    }

    public static async Task StatusAsync(IServiceProvider services, string text)
    {
        try
        {
            await services.GetRequiredService<IJSRuntime>().InvokeVoidAsync("playbookBoot.status", text);
        }
        catch (JSException)
        {
        }
    }

    private static async Task StepAsync(IServiceProvider services, string label, Func<Task> step)
    {
        await StatusAsync(services, label);
        var watch = Stopwatch.StartNew();
        await Quietly(step);
        Console.WriteLine($"Playbook: {label} took {watch.ElapsedMilliseconds} ms");
    }

    /// <summary>Each service falls back to mock data on its own; a failure here must not stop the app.</summary>
    private static async Task Quietly(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Playbook: startup step failed: {ex.Message}");
        }
    }
}
