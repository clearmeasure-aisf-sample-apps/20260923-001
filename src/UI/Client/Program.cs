using ClearMeasure.Bootcamp.Core;
using ClearMeasure.Bootcamp.UI.Client;
using Lamar;
using Lamar.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using Toolbelt.Blazor.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<IHostEnvironment>(new WasmHostEnvironment(builder.HostEnvironment));
var configurationModel = new ConfigurationModel
{
    AppInsightsConnectionString = builder.Configuration[BrowserTelemetry.ConnectionStringKey],
    AppInsightsSamplingPercentage =
        BrowserTelemetry.SamplingPercentage(builder.Configuration[BrowserTelemetry.SamplingPercentageKey])
};

builder.Services.AddBrowserTelemetry(configurationModel);

// Add authentication services
builder.Services.AddAuthorizationCore();
builder.Services.AddSpeechSynthesis();
builder.Services.AddSpeechRecognition();
builder.ConfigureContainer<ServiceRegistry>(
    new LamarServiceProviderFactory(), registry =>
        registry.IncludeRegistry<UIClientServiceRegistry>());


var app = builder.Build();
await BrowserTelemetry.StartAsync(app.Services.GetRequiredService<IJSRuntime>(), configurationModel);
await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
await app.RunAsync();