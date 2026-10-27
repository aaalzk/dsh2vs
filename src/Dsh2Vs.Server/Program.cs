using Dsh2Vs.Build;
using Dsh2Vs.Core;
using Dsh2Vs.Server;
using Dsh2Vs.Server.Tools;
using ModelContextProtocol.AspNetCore;

// Content root is the assembly directory, so appsettings.json is found no matter which
// working directory the service was started from (Task Scheduler, ssh, another tool...).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var options = builder.Configuration.GetSection("Dsh2Vs").Get<Dsh2VsOptions>() ?? new Dsh2VsOptions();
options.WorkspaceRoot = Path.GetFullPath(
    string.IsNullOrWhiteSpace(options.WorkspaceRoot)
        ? Directory.GetCurrentDirectory()
        : options.WorkspaceRoot);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<MsBuildLocator>();
builder.Services.AddSingleton<SolutionParser>();
builder.Services.AddSingleton<ProjectInspector>();
builder.Services.AddSingleton<MsBuildRunner>();
builder.Services.AddSingleton<VsState>();

// Stateless HTTP: every request is self-contained, so a client reconnect after a server restart
// (or after the user clicks something) never hits a stale Mcp-Session-Id.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<StatusTools>()
    .WithTools<ProjectTools>()
    .WithTools<BuildTools>();

var app = builder.Build();

app.MapGet("/healthz", (VsState state) => Results.Json(state.Health()));
app.MapMcp("/mcp");

app.Run();
