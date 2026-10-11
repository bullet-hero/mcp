using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> Prefab Mode and a placement's overrides. </summary>
    [McpServerToolType]
    public sealed class PrefabTools
    {
        private readonly BridgeClient _bridge;

        public PrefabTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "prefab_mode_enter")]
        [Description("Opens a prefab template for editing, as the author's Prefab Mode does. Afterwards every "
                     + "read and edit_batch works inside the template (results say scope prefab:<guid>), the "
                     + "template root is id -3, and level-wide verbs are refused until prefab_mode_exit. "
                     + "Refused: not_found, refused (already open further out, or nested too deep), user_busy.")]
        public Task<CallToolResult> PrefabModeEnter(
            [Description("Template guid, as resources_list prefabs or an outline prefab:<guid> prints it.")]
            string id,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PrefabModeEnter,
                new PrefabModeEnterRequest { Id = id }, token);

        [McpServerTool(Name = "prefab_mode_exit")]
        [Description("Leaves the innermost open template (or all of them). Answers depth: 0 is the level.")]
        public Task<CallToolResult> PrefabModeExit(
            [Description("True to leave every open template at once.")] bool? all = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PrefabModeExit,
                new PrefabModeExitRequest { All = all }, token);

        [McpServerTool(Name = "prefab_overrides", ReadOnly = true)]
        [Description("What a prefab placement changes against its template, one line per override "
                     + "(root Name = \"boss\", #5 Layer = 7, #5 Positions[2] = ...). Takes a placement id or "
                     + "one of its materialized copies (a copy shows only its own lines). Paged.")]
        public Task<CallToolResult> PrefabOverrides(
            [Description("Placement object id, or a copy's id.")] int id,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PrefabOverrides,
                new PrefabOverridesRequest { Id = id, Offset = offset, Limit = limit }, token);
    }
}
