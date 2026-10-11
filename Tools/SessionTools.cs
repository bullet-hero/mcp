using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
                     + "the current revision and session id, whether the author is busy, and what you may do now "
                     + "(write, open levels, save). Cheap and read-only: call it first and whenever a call fails. "
                     + "If the game is not running or MCP is off you get error not_connected: start it with "
                     + "app_launch if the player wants, or ask the author to open the game and enable MCP in "
                     + "Settings.")]
        public Task<CallToolResult> SessionStatus(CancellationToken token)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.SessionStatus, null, token);
    }
}
