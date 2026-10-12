using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> Importing resources, the Raw tree, editor commands and validation. </summary>
    [McpServerToolType]
    public sealed class MaintenanceTools
    {
        private readonly BridgeClient _bridge;

        public MaintenanceTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "resource_import", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Copies a file from disk into the open level's folder and adds it as a resource, as its own "
                     + "undo step (not part of any batch). Texture: png/jpg; font: ttf/otf/ttc; audio: "
                     + "mp3/wav/ogg. Result: the new resource id (negative) and file name. Refused: forbidden "
                     + "(a file of that name is already in the level folder), invalid_params, refused (the file "
                     + "could not be loaded), busy, prefab_mode.")]
        public Task<CallToolResult> ResourceImport(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Absolute path of the source file.")] string path,
            [Description("texture, font or audio.")] string kind,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ResourceImport,
                new ResourceImportRequest { ExpectedRevision = expectedRevision, Path = path, Kind = kind }, token);

        [McpServerTool(Name = "raw_get", ReadOnly = true)]
        [Description("The level file as a tree, by the file's own keys: an empty path lists the roots level and "
                     + "meta; a node answers its kind, summary and children (one line each: path | C# name | "
                     + "kind | summary), a leaf its value in wire form. Walk down by copying child paths. "
                     + "Children are paged.")]
        public Task<CallToolResult> RawGet(
            [Description("Node path copied from a child line, such as level.game.objs[ObjectId=3].name; absent for the roots.")] string path = null,
            [Description("Children to skip.")] int? offset = null,
            [Description("Children to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.RawGet,
                new RawGetRequest { Path = path, Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "raw_set", ReadOnly = false, Destructive = true)]
        [Description("LAST RESORT, after edit_batch: writes one node of the level file in wire form, unvalidated, "
                     + "as one undo step. Result lists validation issues the edit introduced (newIssues); fix "
                     + "or undo them. Refused: user_busy while the author's Raw tab is editing, prefab_mode, "
                     + "busy, invalid_params, not_found.")]
        public Task<CallToolResult> RawSet(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Node path copied from raw_get.")] string path,
            [Description("The new value in the file's wire form, as raw_get shows a leaf.")] JsonElement value,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.RawSet, new RawSetRequest
            {
                ExpectedRevision = expectedRevision,
                Path = path,
                Value = BridgeTool.Time(value),
            }, token);

        [McpServerTool(Name = "command_list", ReadOnly = true)]
        [Description("Editor commands the AI may run, one line each: id, available or not right now, and what "
                     + "it does. all: true adds the excluded ones with the reason and the tool to use instead. "
                     + "Paged.")]
        public Task<CallToolResult> CommandList(
            [Description("True to list the excluded commands too.")] bool? all = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.CommandList,
                new CommandListRequest { All = all, Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "command_run", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Runs one allowed editor command (see command_list), e.g. cmd_editor_duplicate on the "
                     + "selection set with selection_set. changed is false when it ran but edited nothing; created "
                     + "lists the ids it made, warnings what will not play (a copy outside its parent's span). "
                     + "Refused: forbidden (excluded), not_found, refused (unavailable now), busy, stale, "
                     + "user_busy.")]
        public Task<CallToolResult> CommandRun(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Command id from command_list.")] string id,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.CommandRun,
                new CommandRunRequest { ExpectedRevision = expectedRevision, Id = id }, token);

        [McpServerTool(Name = "validate_list", ReadOnly = true)]
        [Description("The level's validation findings, most severe first, one line each: rule severity "
                     + "root:path - message, plus error/warning/advice counts. The first word is the rule name "
                     + "validate_fix takes. Paged.")]
        public Task<CallToolResult> ValidateList(
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.Validate,
                new AgentPagedRequest { Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "validate_fix", ReadOnly = false, Destructive = true)]
        [Description("Applies the automatic fixes of the validation rules as one undo step (nothing is written "
                     + "when nothing needs fixing). Result: fixedCount, fixes (rule root:path - before -> "
                     + "after), remaining. Refused: user_busy while the author's Rules tab is open, "
                     + "prefab_mode, busy, stale.")]
        public Task<CallToolResult> ValidateFix(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Only these rule names (from validate_list); absent for all.")] string[] rules = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ValidateFix, new ValidateFixRequest
            {
                ExpectedRevision = expectedRevision,
                Rules = rules?.ToList(),
            }, token);
    }
}
