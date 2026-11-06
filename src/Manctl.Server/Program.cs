using Manctl.Server;
using Manctl.Server.Guard;
using Manctl.Core.Automation;
using Manctl.Core.Commands;
using Manctl.Core.Processes;
using Manctl.Core.Win32;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
Console.Error.WriteLine($"manctl v{version} — by 4ByteDev");

// Must happen before any window/COM interaction so GetWindowRect, UIA bounding rects and SendInput
// coordinates all agree on the same physical-pixel space, regardless of per-monitor DPI scaling.
WindowManager.EnablePerMonitorDpiAwareness();

var useHttp = args.Contains("--http");

if (useHttp)
{
    await RunHttpAsync(args);
}
else
{
    await RunStdioAsync(args);
}
return;

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // stdio is the transport, so stdout is reserved for the MCP protocol — all logs must go to stderr.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    RegisterCoreServices(builder.Services);
    builder.Services
        .AddMcpServer(options => options.ServerInstructions = ServerRules.Text)
        .WithStdioServerTransport()
        .WithGuardedToolsFromAssembly(typeof(Program).Assembly);

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var port = GetArgValue(args, "--port") is { } p && int.TryParse(p, out var parsed) ? parsed : 3001;
    var token = Environment.GetEnvironmentVariable("MANCTL_TOKEN") ?? Guid.NewGuid().ToString("N");

    var builder = WebApplication.CreateBuilder(args);
    RegisterCoreServices(builder.Services);
    builder.Services
        .AddMcpServer(options => options.ServerInstructions = ServerRules.Text)
        .WithHttpTransport()
        .WithGuardedToolsFromAssembly(typeof(Program).Assembly);

    var app = builder.Build();

    // Loopback-only by construction (bound to 127.0.0.1 below) plus a bearer token — this is full mouse/keyboard/
    // process control, so it must never be reachable from the network and never usable without the token.
    app.Use(async (context, next) =>
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header != $"Bearer {token}")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid bearer token.");
            return;
        }
        await next();
    });

    app.MapMcp();

    Console.Error.WriteLine($"manctl listening on http://127.0.0.1:{port} (loopback only)");
    Console.Error.WriteLine($"Authorization: Bearer {token}");

    await app.RunAsync($"http://127.0.0.1:{port}");
}

static void RegisterCoreServices(IServiceCollection services)
{
    services.AddSingleton<AutomationContext>();
    services.AddSingleton<ProcessManager>();
    services.AddSingleton<CommandExecutionService>();
}

static string? GetArgValue(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}
