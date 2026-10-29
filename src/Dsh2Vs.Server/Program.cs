using Dsh2Vs.Build;
using Dsh2Vs.Core;
using Dsh2Vs.Debug;
using Dsh2Vs.Server;
using Dsh2Vs.Server.Tools;
using Dsh2Vs.Server.VsDte;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

// Content root is the assembly directory, so appsettings.json is found no matter which
// working directory the service was started from (Task Scheduler, ssh, another tool...).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var options = builder.Configuration.GetSection("Dsh2Vs").Get<Dsh2VsOptions>() ?? new Dsh2VsOptions();
if (!string.IsNullOrWhiteSpace(options.WorkspaceRoot))
{
    options.WorkspaceRoot = Path.GetFullPath(options.WorkspaceRoot);
}

var debugOptions = builder.Configuration.GetSection("Dsh2Vs:Debug").Get<DebugOptions>() ?? new DebugOptions();

// Shared by both transports: the bridge is stateless per call, so HTTP and stdio behave identically.
void RegisterServices(IServiceCollection services)
{
    services.AddSingleton(options);
    services.AddSingleton(debugOptions);
    services.AddSingleton<WorkspaceRegistry>();
    services.AddSingleton<MsBuildLocator>();
    services.AddSingleton<SolutionParser>();
    services.AddSingleton<ProjectInspector>();
    services.AddSingleton<MsBuildRunner>();
    services.AddSingleton<VsState>();
    services.AddSingleton<CdbLocator>();
    services.AddSingleton<CrashAnalyzer>();
    services.AddSingleton<CrashForensics>();
    services.AddSingleton<DebugSessionManager>();
    services.AddSingleton<VsDteClient>();
}

IMcpServerBuilder RegisterTools(IMcpServerBuilder server) =>
    server.WithTools<StatusTools>()
        .WithTools<WorkspaceTools>()
        .WithTools<ProjectTools>()
        .WithTools<BuildTools>()
        .WithTools<DebugTools>()
        .WithTools<VsDteTools>();

// --- stdio transport (for MCPB bundles and clients that spawn the process) ----------------
// stdout is the protocol channel here, so nothing else may write to it: host logging goes to
// stderr only, and the web pipeline (healthz, /mcp) is not started at all.
if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
{
    var stdioBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args.Where(a => !a.Equals("--stdio", StringComparison.OrdinalIgnoreCase)).ToArray(),
        ContentRootPath = AppContext.BaseDirectory,
    });

    stdioBuilder.Logging.ClearProviders();
    stdioBuilder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    RegisterServices(stdioBuilder.Services);
    RegisterTools(stdioBuilder.Services.AddMcpServer().WithStdioServerTransport());

    await stdioBuilder.Build().RunAsync();
    return;
}

// --- HTTP transport (default) -------------------------------------------------------------
// Stateless HTTP: every request is self-contained, so a client reconnect after a server restart
// (or after the user clicks something) never hits a stale Mcp-Session-Id.
RegisterServices(builder.Services);
RegisterTools(builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless));

var app = builder.Build();

app.MapGet("/healthz", (VsState state) => Results.Json(state.Health()));
app.MapMcp("/mcp");

app.Run();
