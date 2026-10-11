using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Playbook.Application;
using Playbook.Client;
using Playbook.Infrastructure;
using Playbook.Web.Components;
using Playbook.Web.Features.QuickPicks.Interfaces;
using Playbook.Web.Features.QuickPicks.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The server's settings, then the browser's overrides (see the .csproj).
foreach (var name in new[] { "appsettings.json", "browser-settings.json" })
{
    builder.Configuration.AddJsonStream(typeof(RelayHandler).Assembly.GetManifestResourceStream(name)!);
}

builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();
builder.Services.AddSingleton<IQuickPicksBoard, QuickPicksBoard>();

// Outgoing calls to the football data APIs go through the site's relay (see RelayHandler).
var appBase = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddTransient(_ => new RelayHandler(appBase));
builder.Services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler<RelayHandler>());
builder.Services.AddSingleton<BrowserDataStore>();

var host = builder.Build();

// Put the saved data directory back, load the data the pages read synchronously, then start the
// background work the server would run as hosted services. The loading screen in index.html
// stays up until the app renders.
var store = host.Services.GetRequiredService<BrowserDataStore>();
await Boot.StatusAsync(host.Services, "Restoring saved data");
await store.RestoreAsync();
await Boot.WarmUpAsync(host.Services);
store.StartSaving(TimeSpan.FromSeconds(10));

foreach (var service in host.Services.GetServices<IHostedService>())
{
    await service.StartAsync(CancellationToken.None);
}

await host.RunAsync();
