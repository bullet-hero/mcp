using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> The author's selection in the editor. </summary>
    [McpServerToolType]
    public sealed class SelectionTools
    {
        private readonly BridgeClient _bridge;

        public SelectionTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "selection_get", ReadOnly = true)]
        [Description("The object ids the author has selected right now: what they mean by \"this\" or \"these\".")]
        public Task<CallToolResult> SelectionGet(CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.SelectionGet, null, token);

        [McpServerTool(Name = "selection_set")]
        [Description("Replaces the author's selection, e.g. to show what you changed. An empty list clears it. "
                     + "Nothing is selected if any id does not exist. Refused with user_busy mid-drag.")]
        public Task<CallToolResult> SelectionSet(
            [Description("Object ids to select.")] int[] ids,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.SelectionSet,
                new SelectionSetRequest { Ids = ids?.ToList() ?? new List<int>() }, token);
    }
}
