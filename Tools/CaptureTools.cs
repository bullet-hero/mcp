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

    /// <summary> Real pixels through the author's shared playhead, and the playhead itself. </summary>
    [McpServerToolType]
    public sealed class CaptureTools
    {
        private const string FrameDoc = "Global frame from 1 (120 or \"f:120\") or seconds (\"s:12.5\").";
        private const string Refusals = "Refused: user_busy (the author is working: retry later), preempted "
                                        + "(the author touched the editor mid-capture; the playhead stays where "
                                        + "they put it), busy (a save or export), prefab_mode.";

        private readonly BridgeClient _bridge;

        public CaptureTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "view_capture")]
        [Description("A PNG of the real game picture at a frame, through the level camera with effects and "
                     + "post-processing, editor overlays hidden. It MOVES the author's playhead for a moment "
                     + "and, by the author's setting, puts it back or leaves it there; playhead in the result "
                     + "is where it ended. Prefer view_schematic for layout checks. " + Refusals)]
        public Task<CallToolResult> ViewCapture(
            [Description(FrameDoc)] JsonElement frame,
            [Description("Longest side in pixels, capped by the game's image limit.")] int? size = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.ViewCapture,
                new ViewCaptureRequest { Frame = BridgeTool.Time(frame), Size = size }, token);

        [McpServerTool(Name = "view_filmstrip")]
        [Description("Several real frames as one labelled contact sheet (f<frame> under each cell): either "
                     + "frames, or from/to with count spread evenly, ends included (default 6, max 16). "
                     + "Captures one after another through the playhead like view_capture; one interruption "
                     + "aborts the whole strip. " + Refusals)]
        public Task<CallToolResult> ViewFilmstrip(
            [Description("First frame of the range. " + FrameDoc)] JsonElement? from = null,
            [Description("Last frame of the range. " + FrameDoc)] JsonElement? to = null,
            [Description("Frames spread over the range, default 6, max 16.")] int? count = null,
            [Description("Exact frames instead of a range, in display order, e.g. [120, \"s:5\"].")]
            JsonElement? frames = null,
            [Description("Longest side of the whole sheet in pixels, capped by the game's image limit.")]
            int? size = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.ViewFilmstrip, new ViewFilmstripRequest
            {
                From = BridgeTool.Time(from),
                To = BridgeTool.Time(to),
                Count = count,
                Frames = (BridgeTool.Time(frames) as JArray)?.ToList(),
                Size = size,
            }, token);

        [McpServerTool(Name = "playhead_get", ReadOnly = true)]
        [Description("The author's playhead: frame, playing, and whether the author is busy right now.")]
        public Task<CallToolResult> PlayheadGet(CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PlayheadGet, null, token);

        [McpServerTool(Name = "playhead_set")]
        [Description("Moves the author's playhead to a frame, paused, to show the author something. Only "
                     + "while the author is idle: refused with user_busy otherwise.")]
        public Task<CallToolResult> PlayheadSet(
            [Description(FrameDoc)] JsonElement frame,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PlayheadSet,
                new PlayheadSetRequest { Frame = BridgeTool.Time(frame) }, token);
    }
}
