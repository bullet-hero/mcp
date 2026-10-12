using BH.SDK;
using BH.SDK.Agent;
using BH.MCP;
using BH.MCP.Prompts;
using BH.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

var endpoint = Argument(args, "--endpoint") ?? AgentEndpoint.DefaultPath();

// A client always pipes stdin; a console that did not (a double click) gets the log viewer instead of
// a server waiting for JSON on the keyboard. --stdio forces the server, --logs the viewer.
if (HasFlag(args, "--logs") || (!Console.IsInputRedirected && !HasFlag(args, "--stdio")))
    return LogViewer.Run(endpoint, ServerVersion());

var logLevel = Enum.TryParse<LogLevel>(Argument(args, "--log-level"), true, out var parsed)
    ? parsed
    : LogLevel.Information;

// stdout belongs to the MCP transport: every log line goes to stderr, and to the shared log file.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(logLevel);

// The SDK and the host log every request twice over at Information; the server's own line says it once.
var frameworkLevel = logLevel <= LogLevel.Debug ? logLevel : LogLevel.Warning;
builder.Logging.AddFilter("ModelContextProtocol", frameworkLevel);
builder.Logging.AddFilter("Microsoft", frameworkLevel);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddProvider(new FileLoggerProvider(ServerLog.PathFor(endpoint), logLevel));

var bridge = new BridgeClient(endpoint);
builder.Services.AddSingleton(bridge);
builder.Services.AddSingleton(new GameLauncher(bridge, Argument(args, "--game-exe")));

// Parameter types are resolved from source-generated metadata: a trimmed binary has no reflection JSON.
var toolJson = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
toolJson.TypeInfoResolverChain.Add(ToolJsonContext.Default);

// Every tool class is listed: WithToolsFromAssembly() finds nothing once the binary is trimmed.
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "bullet-hero",
            Title = "Bullet Hero MCP",
            Version = ServerVersion(),
            Description = $"Bullet Hero game and level editor bridge, protocol {AgentProtocol.Version}, BH.SDK {SdkVersion.Value}",
            WebsiteUrl = "https://bullethero.space/",
            Icons = [new Icon { Source = IconDataUri(), MimeType = "image/png", Sizes = ["64x64"] }],
        };
        options.ServerInstructions = ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithTools<SessionTools>(toolJson)
    .WithTools<AppTools>(toolJson)
    .WithTools<LevelTools>(toolJson)
    .WithTools<ReadTools>(toolJson)
    .WithTools<EditTools>(toolJson)
    .WithTools<HistoryTools>(toolJson)
    .WithTools<SelectionTools>(toolJson)
    .WithTools<PrefabTools>(toolJson)
    .WithTools<GeneratorTools>(toolJson)
    .WithTools<MaintenanceTools>(toolJson)
    .WithTools<VisionTools>(toolJson)
    .WithTools<CaptureTools>(toolJson)
    .WithTools<MusicTools>(toolJson)
    .WithTools<ContextTools>(toolJson)
    .WithPrompts<WorkflowPrompts>(toolJson)
    .WithRequestFilters(filters =>
    {
        // An argument the binder cannot read is the AI's mistake to fix, not "An error occurred": name it.
        filters.AddCallToolFilter(next => async (context, token) =>
        {
            try
            {
                return await next(context, token).ConfigureAwait(false);
            }
            catch (Exception e) when (ArgumentErrors.Describe(e) is { } message)
            {
                return BridgeTool.Error(AgentErrorCode.InvalidParams, $"{context.Params?.Name}: {message}");
            }
        });
        filters.AddGetPromptFilter(next => async (context, token) =>
        {
            try
            {
                return await next(context, token).ConfigureAwait(false);
            }
            catch (Exception e) when (ArgumentErrors.Describe(e) is { } message)
            {
                throw new McpProtocolException($"{context.Params?.Name}: {message}", McpErrorCode.InvalidParams);
            }
        });
    });

var app = builder.Build();
bridge.Log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BH.MCP.Calls");
bridge.Log.LogInformation("Server {Version} started (pid {Pid}), endpoint {Endpoint}", ServerVersion(),
    Environment.ProcessId, endpoint);

await app.RunAsync();
return 0;

// The server's own version: <Version> in BH.MCP.csproj, the one copy the release workflow checks.
static string ServerVersion()
    => typeof(BridgeClient).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
       ?? "0.0.0";

// The icon travels inline: a client may show it with no file or network access.
static string IconDataUri()
{
    using var stream = typeof(BridgeClient).Assembly.GetManifestResourceStream("icon-64.png");
    using var memory = new System.IO.MemoryStream();
    stream.CopyTo(memory);
    return "data:image/png;base64," + System.Convert.ToBase64String(memory.ToArray());
}

static string Argument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];

    return null;
}

static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;
