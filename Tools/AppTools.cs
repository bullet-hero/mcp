using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> The game as a whole: its screens, and starting it. </summary>
    [McpServerToolType]
    public sealed class AppTools
    {
        private const string NoScreen = "none";

        private readonly BridgeClient _bridge;
        private readonly GameLauncher _launcher;

        public AppTools(BridgeClient bridge, GameLauncher launcher)
        {
            _bridge = bridge;
            _launcher = launcher;
        }

        /// <summary> The bridge's <c>app.navigate</c>. </summary>
        [McpServerTool(Name = "app_navigate", ReadOnly = false, Destructive = false)]
        [Description("Move the running game to another screen with a line of its launch arguments, exactly as if "
                     + "it were started with them: '--editor', '--editor --level <id or folder>', '--level <id>' "
                     + "(the level's page in the menu), '--settings [tab]', '--menu', '--level <id> --game "
                     + "[--speed --lives --seed --bot --checkpoints --no-checkpoints --no-collision]'. --game starts "
                     + "a real run that counts in the player's statistics: use it only when the player asked for "
                     + "a run. --autosave, --suppress-game-saves and --frame-stats apply to a whole launch and are "
                     + "refused here (see app_launch). Refused with unsaved_changes while the editor's level is "
                     + "unsaved, user_busy while the author drags or the player is in a run. With --level it "
                     + "waits until the level is open, or warns that no level matched (an id or a folder name, "
                     + "not the display name). Returns the screen and the warnings; then call session_status.")]
        public Task<CallToolResult> AppNavigate(
            [Description("Launch arguments in command-line syntax; a value with spaces goes in double quotes.")]
            string args,
            CancellationToken token)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.AppNavigate, new { args }, token);

        /// <summary> Starts the game when it is not running. </summary>
        [McpServerTool(Name = "app_launch", ReadOnly = false, Destructive = false)]
        [Description("Start Bullet Hero when it is not running, with a line of its launch arguments (the same "
                     + "syntax as app_navigate, plus --autosave on|off, --suppress-game-saves, --frame-stats). "
                     + "Only at the player's request. Works only for an installed game next to this server, and "
                     + "only if MCP is enabled in the game's settings. Waits for the game's bridge, up to "
                     + "90 s, and returns the session status.")]
        public async Task<CallToolResult> AppLaunch(
            [Description("Launch arguments in command-line syntax, such as '--editor'; empty opens the menu.")]
            string args = "",
            CancellationToken token = default)
        {
            var running = GameLauncher.RunningPid(_bridge.EndpointPath);
            if (running.HasValue)
                return BridgeTool.Error(AgentErrorCode.Refused,
                    $"Bullet Hero is already running (pid {running.Value}); use app_navigate.");

            var game = _launcher.Locate();
            if (game == null)
                return BridgeTool.Error(AgentErrorCode.NotFound,
                    "No game executable beside the server. A development server (BH.MCP/bin) has no game next "
                    + "to it: start the game or Unity Play mode by hand, or set --game-exe <path> in the client "
                    + "config.");

            var silent = GameLauncher.RunningGamePid(game, GameLauncher.CurrentPlatform());
            if (silent.HasValue)
                return BridgeTool.Error(AgentErrorCode.Refused,
                    $"Bullet Hero is already running (pid {silent.Value}) but its MCP bridge does not answer: MCP "
                    + "may be off in Settings -> Game Editor -> AI Agent (MCP), or the game is still loading. Ask the author; "
                    + "never start a second copy.");

            // No game runs, so a selected one is gone: the started game is the default.
            await _bridge.SelectAsync(null).ConfigureAwait(false);

            var since = DateTime.UtcNow;
            try
            {
                GameLauncher.Start(game, args, GameLauncher.CurrentPlatform());
            }
            catch (Exception e)
            {
                return BridgeTool.Error(AgentErrorCode.Internal, $"Could not start {game}: {e.Message}");
            }

            if (!await _launcher.WaitForEndpointAsync(since, token).ConfigureAwait(false))
                return BridgeTool.Error(AgentErrorCode.NotConnected,
                    $"The game was started but opened no MCP bridge within {GameLauncher.EndpointWaitSeconds} s: "
                    + "MCP may be off in Settings -> Game Editor -> AI Agent (MCP), or it is still loading. Call "
                    + "session_status in a moment.");

            var status = await StatusOnceAScreenIsUp(since, OpensALevel(args), token).ConfigureAwait(false);
            var result = new JObject { ["started"] = true, ["game"] = game, ["session"] = status };
            return BridgeTool.Text(result.ToString(Formatting.None), false);
        }

        // "--editor --level x": the level loads after the screen is up, and the caller wants it open.
        private static bool OpensALevel(string args)
        {
            var tokens = AgentLaunchLine.Split(args);
            return Array.IndexOf(tokens, "--editor") >= 0 && Array.IndexOf(tokens, "--level") >= 0;
        }

        // The bridge listens before the first screen is built, so the status is polled until one is up (and,
        // for a level target, until the level is open).
        private async Task<JToken> StatusOnceAScreenIsUp(DateTime since, bool level, CancellationToken token)
        {
            var deadline = since.AddSeconds(GameLauncher.EndpointWaitSeconds);
            JToken status = null;

            while (true)
            {
                try
                {
                    status = await _bridge.CallAsync(AgentProtocol.Methods.SessionStatus, null, token)
                        .ConfigureAwait(false);
                    if ((string)status?["screen"] is { } screen && screen != NoScreen
                        && (!level || status["levelId"] != null))
                        return status;
                }
                catch (BridgeException e)
                {
                    status = new JObject { ["error"] = e.Code, ["message"] = e.Message };
                }

                if (DateTime.UtcNow >= deadline) return status;
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }
}
