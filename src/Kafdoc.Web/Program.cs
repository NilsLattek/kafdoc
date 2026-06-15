using Kafdoc.Web.Components;
using Kafdoc.Infrastructure;
using Kafdoc.Application;
using Kafdoc.Domain;

using Markdig;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.ConfigureInfrastructure(builder.Configuration);
builder.Services.ConfigureDomain(builder.Configuration);
builder.Services.ConfigureApplication(builder.Configuration);
builder.Services.AddSingleton(new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .DisableHtml()
    .Build());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

#pragma warning disable S6966 // Awaitable method should be used
app.Run();
#pragma warning restore S6966 // Awaitable method should be used

#pragma warning disable S1118 // Top-level Program class used as assembly anchor for integration tests
/// <summary>Exposes the implicit entry-point class so tests can anchor to the Web assembly.</summary>
public partial class Program;
#pragma warning restore S1118
