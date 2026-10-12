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

    /// <summary> The operation history and what changed since a revision. </summary>
    [McpServerToolType]
    public sealed class HistoryTools
    {
        private readonly BridgeClient _bridge;

        public HistoryTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "history_get", ReadOnly = true)]
        [Description("The author's undo history, one line per step, AI batches shown with their caption. "
                     + "Pages: header total/offset/next, lines after it.")]
        public Task<CallToolResult> HistoryGet(
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.HistoryGet,
                new HistoryGetRequest { Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "history_undo", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Undoes the last history step, whoever made it: the author's Ctrl+Z. One AI batch is one "
                     + "step. applied is false when there is nothing to undo; step names what was undone, as "
                     + "history_get words it. Refused while the editor is locked or the author is dragging.")]
        public Task<CallToolResult> HistoryUndo(
            [Description("Revision of your last read; a mismatch is stale. Absent to undo regardless.")]
            int? expectedRevision = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.HistoryUndo,
                new HistoryStepRequest { ExpectedRevision = expectedRevision }, token);

        [McpServerTool(Name = "history_redo", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Redoes the last undone step (the author's Ctrl+Y). applied is false when there is nothing "
                     + "to redo; step names what was redone. Same refusals as history_undo.")]
        public Task<CallToolResult> HistoryRedo(
            [Description("Revision of your last read; a mismatch is stale. Absent to redo regardless.")]
            int? expectedRevision = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.HistoryRedo,
                new HistoryStepRequest { ExpectedRevision = expectedRevision }, token);

        [McpServerTool(Name = "changes_since", ReadOnly = true)]
        [Description("What changed after a revision, oldest first, as JSON: revision, source (ai or author), "
                     + "operation name key, caption of an AI batch. Use it after stale to see what the author "
                     + "did. truncated true means the log is too short: re-read what matters.")]
        public Task<CallToolResult> ChangesSince(
            [Description("The last revision you have seen.")] int revision,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ChangesSince,
                new ChangesSinceRequest { Revision = revision }, token);
    }
}
