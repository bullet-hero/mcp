using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> The editor's scope generators: list, schema, run. </summary>
    [McpServerToolType]
    public sealed class GeneratorTools
    {
        private const string TimeDoc = "Global frame from 1 (120 or \"f:120\"), seconds (\"s:12.5\"), a marker "
            + "(\"marker:drop\") or, with a beat grid, \"bar:17\".";

        private readonly BridgeClient _bridge;

        public GeneratorTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "generator_list", ReadOnly = true)]
        [Description("Generators that fill or modify a time window of the level (bullet patterns, grids, "
                     + "modifiers), one line each: id [Content|Modifier] Name (needs) - description. Paged. "
                     + "Read generator_schema before generator_run.")]
        public Task<CallToolResult> GeneratorList(
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.GeneratorList,
                new AgentPagedRequest { Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "generator_schema", ReadOnly = true)]
        [Description("A generator's parameters as a JSON schema in form order: type, default, minimum/maximum, "
                     + "enum, x-unit, x-step, x-visible. x-hostFilled fields are filled by the editor and "
                     + "cannot be set.")]
        public Task<CallToolResult> GeneratorSchema(
            [Description("Generator id from generator_list.")] string id,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.GeneratorSchema,
                new GeneratorSchemaRequest { Id = id }, token);

        [McpServerTool(Name = "generator_run", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Runs a generator over [start, end) as ONE undo step (History: \"Generator: <id>\" or your "
                     + "caption). Parameters not given keep their defaults; out-of-range numbers are clamped "
                     + "with a warning; an unknown field is invalid_params. Result: createdCount and created as "
                     + "id runs (\"41-104\"). Refused: stale, user_busy, locked, refused (reason; nothing "
                     + "changed), e.g. a dangerous generator without confirmDangerous or a modifier with no "
                     + "selection.")]
        public Task<CallToolResult> GeneratorRun(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Generator id from generator_list.")] string id,
            [Description("First frame covered. " + TimeDoc)] JsonElement start,
            [Description("First frame NOT covered (the last frame + 1 at most). " + TimeDoc)] JsonElement end,
            [Description("Field name to value, in the schema's types (readable values with a kind where "
                         + "polymorphic).")]
            JsonElement? parameters = null,
            [Description("Object id to create under; absent for the roots.")] int? parent = null,
            [Description("Effective layer for created objects.")] int? layer = null,
            [Description("Random seed, 0 to 4294967295; default 0.")] long? seed = null,
            [Description("Object ids a modifier works on.")] int[] selection = null,
            [Description("Name of the group created objects go under (Content generators).")]
            string groupName = null,
            [Description("Give each created layer its own group (Content generators).")] bool? splitLayers = null,
            [Description("Required true for a generator the editor asks the author to confirm.")]
            bool? confirmDangerous = null,
            [Description("What the History tab shows for this step.")] string caption = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.GeneratorRun, new GeneratorRunRequest
            {
                ExpectedRevision = expectedRevision,
                Id = id,
                Start = BridgeTool.Time(start),
                End = BridgeTool.Time(end),
                Parameters = BridgeTool.Time(parameters) as JObject,
                Parent = parent,
                Layer = layer,
                Seed = seed,
                Selection = selection?.ToList(),
                GroupName = groupName,
                SplitLayers = splitLayers,
                ConfirmDangerous = confirmDangerous,
                Caption = caption,
            }, token);
    }
}
