using Playbook.Application;
using Playbook.Infrastructure;
using Playbook.Web.Components;
using Playbook.Web.Features.QuickPicks.Interfaces;
using Playbook.Web.Features.QuickPicks.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();

builder.Services.AddSingleton<IQuickPicksBoard, QuickPicksBoard>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Skip HTTPS redirection in Development so phone/tunnel HTTP access works.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
