using System.ComponentModel;
using System.Linq;
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

    /// <summary> Connection and session tools. </summary>
    [McpServerToolType]
    public sealed class SessionTools
    {
        private readonly BridgeClient _bridge;

        public SessionTools(BridgeClient bridge) => _bridge = bridge;

        /// <summary> The bridge's <c>session.status</c>. </summary>
        [McpServerTool(Name = "session_status", ReadOnly = true)]
        [Description("Whether the Bullet Hero game answers, which screen it shows, the open level's id and name, "
                     + "the current revision and session id, whether the author is busy (playing counts: "
                     + "playback_set pauses), and what you may do now "
                     + "(write, open levels, save). Cheap and read-only: call it first and whenever a call fails. "
                     + "If the game is not running or MCP is off you get error not_connected: start it with "
                     + "app_launch if the player wants, or ask the author to open the game and enable MCP in "
                     + "Settings -> Game editor -> MCP.")]
        public Task<CallToolResult> SessionStatus(CancellationToken token)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.SessionStatus, null, token);

        /// <summary> Every running game with MCP on, read from the per-game endpoint files. </summary>
        [McpServerTool(Name = "instance_list", ReadOnly = true)]
        [Description("Every running Bullet Hero game with MCP on: pid, port, game version, whether it is the "
                     + "default (the last one started) and whether your calls go to it. With one game you never "
                     + "need this; with several, instance_select picks which one every later call goes to.")]
        public CallToolResult InstanceList()
        {
            var defaultEndpoint = DefaultEndpoint();
            var defaultToken = defaultEndpoint?.Token;

            // No pid chosen means the calls follow the default game.
            var targetPid = _bridge.SelectedPid ?? defaultEndpoint?.Pid;
            var array = new JArray(LiveInstances().Select(e => new JObject
            {
                ["pid"] = e.Pid,
                ["port"] = e.Port,
                ["gv"] = e.Gv,
                ["isDefault"] = e.Token == defaultToken,
                ["isSelected"] = targetPid == e.Pid,
            }));

            return BridgeTool.Text(new JObject { ["instances"] = array }.ToString(Formatting.None), false);
        }

        /// <summary> Points this server at one running game, or back at the default. </summary>
        [McpServerTool(Name = "instance_select")]
        [Description("Send every later call to the game with this pid (from instance_list). Without a pid, go "
                     + "back to the default: the last game started. Each game takes one client at a time.")]
        public async Task<CallToolResult> InstanceSelect(
            [Description("The game's pid from instance_list; omit to go back to the default.")]
            int? pid = null)
        {
            var instances = LiveInstances();
            if (pid is { } wanted && instances.All(e => e.Pid != wanted))
            {
                var live = string.Join(", ", instances.Select(e => e.Pid));
                return BridgeTool.Error(AgentErrorCode.InvalidParams,
                    $"No running game has pid {wanted}; running: {(live.Length == 0 ? "none" : live)}.");
            }

            await _bridge.SelectAsync(pid).ConfigureAwait(false);
            var result = new JObject { ["selected"] = pid, ["endpoint"] = _bridge.TargetPath };
            return BridgeTool.Text(result.ToString(Formatting.None), false);
        }

        // A crashed game leaves its file behind; only a live pid counts.
        private AgentEndpoint[] LiveInstances()
            => AgentEndpoint.ListInstances(_bridge.EndpointPath).Where(e => GameLauncher.IsAlive(e.Pid)).ToArray();

        private AgentEndpoint DefaultEndpoint()
            => AgentEndpoint.TryRead(_bridge.EndpointPath, out var endpoint, out _) ? endpoint : null;
    }
}
