using System.Collections.Generic;
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

    /// <summary> Seeing the level without the playhead: schematic pictures, frame counts, where an object
    /// is, why it is not drawn, and the shape atlas. </summary>
    [McpServerToolType]
    public sealed class VisionTools
    {
        private const string FrameDoc = "Global frame from 1 (120 or \"f:120\"), seconds (\"s:12.5\"), a marker "
            + "(\"marker:drop\") or, with a beat grid, \"bar:17\".";

        private readonly BridgeClient _bridge;

        public VisionTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "view_schematic", ReadOnly = true)]
        [Description("A PNG schematic of any frame, computed off screen: the author's playhead does not move. "
                     + "Shapes are filled with their mean colour at half alpha, colliders (what hurts the "
                     + "player) outlined red, the camera rect yellow, #id at object centres. Texts and effects "
                     + "are not drawn. Use it before and after edits; view_capture shows the real look. "
                     + "Refused: prefab_mode (it draws the level), invalid_params.")]
        public Task<CallToolResult> ViewSchematic(
            [Description(FrameDoc)] JsonElement frame,
            [Description("World rect {x, y, width, height}, x/y its lower-left corner; absent for the "
                         + "camera's.")] JsonElement? region = null,
            [Description("Longest side in pixels, capped by the game's image limit.")] int? size = null,
            [Description("False to leave out the #id labels.")] bool? labels = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.ViewSchematic, new ViewSchematicRequest
            {
                Frame = BridgeTool.Time(frame),
                Region = BridgeTool.Time(region)?.ToObject<AgentRegion>(),
                Size = size,
                Labels = labels,
            }, token);

        [McpServerTool(Name = "frame_stats", ReadOnly = true)]
        [Description("Counts over a time range, one line per sampled frame: "
                     + "f120 active:34 shapes:20 colliders:12 hazard:8.5% (hazard = share of the camera rect "
                     + "covered by colliders). The step widens when the range holds too many samples; note "
                     + "says so. Use it to find empty or overloaded stretches.")]
        public Task<CallToolResult> FrameStats(
            [Description("First frame; the level's first when absent. " + FrameDoc)] JsonElement? from = null,
            [Description("Last frame; the level's last when absent. " + FrameDoc)] JsonElement? to = null,
            [Description("Frames between samples (60, \"f:60\" or \"s:1\"); 1 when absent.")]
            JsonElement? step = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.FrameStats, new FrameStatsRequest
            {
                From = BridgeTool.Time(from), To = BridgeTool.Time(to), Step = BridgeTool.Time(step),
            }, token);

        [McpServerTool(Name = "cursor_resolve", ReadOnly = true)]
        [Description("Where objects are at one frame: world corners, centre, pivot (what a world-space "
                     + "position key sets), size, rotation, effective layer, the authored and the resolved "
                     + "span, plays, active, inFrame (built into that frame's state; on or off camera alike: "
                     + "object_explain says off_camera). Geometry is absent when the object is not in the frame. "
                     + "Ids that do not exist come back in missing.")]
        public Task<CallToolResult> CursorResolve(
            [Description("Object ids, up to 500.")] int[] ids,
            [Description(FrameDoc)] JsonElement frame,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.CursorResolve, new CursorResolveRequest
            {
                Ids = ids?.ToList() ?? new List<int>(), Frame = BridgeTool.Time(frame),
            }, token);

        [McpServerTool(Name = "object_explain", ReadOnly = true)]
        [Description("Why an object is or is not drawn at a frame: the first reason that applies, in order "
                     + "outside_span, resolved_away (a parent's span clips it), orphan, not_in_frame or "
                     + "capacity_overflow, not_drawable (a rect group), zero_size, inactive, no_mesh, no_effect, "
                     + "transparent, off_camera, capacity_overflow, else drawn; detail gives the numbers.")]
        public Task<CallToolResult> ObjectExplain(
            [Description("Object id.")] int id,
            [Description(FrameDoc)] JsonElement frame,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ObjectExplain,
                new ObjectExplainRequest { Id = id, Frame = BridgeTool.Time(frame) }, token);

        [McpServerTool(Name = "shapes_atlas", ReadOnly = true)]
        [Description("A PNG contact sheet of shapes with a number under each cell; lines map each number to "
                     + "its shape id and name (the id edit_batch shape takes). Same filters as shapes_find. "
                     + "Paged: a page holds what fits the image.")]
        public Task<CallToolResult> ShapesAtlas(
            [Description("Geometric form, as shapes_find names it.")] string form = null,
            [Description("Sector, as shapes_find names it.")] string sector = null,
            [Description("Thickness, as shapes_find names it.")] string thickness = null,
            [Description("Name fragment, case-insensitive.")] string name = null,
            [Description("Shapes to skip.")] int? offset = null,
            [Description("Shapes to return; capped by what fits the image.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.ShapesAtlas, new ShapesAtlasRequest
            {
                Form = form, Sector = sector, Thickness = thickness, Name = name, Offset = offset, Limit = limit,
            }, token);
    }
}
