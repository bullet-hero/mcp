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
        private const string FrameDoc = "Global frame from 1 (120 or \"f:120\"), seconds (\"s:12.5\"), a marker "
            + "(\"marker:drop\") or, with a beat grid, \"bar:17\".";
        private const string Refusals = "Refused: user_busy (the author is working, or the editor is playing: "
                                        + "playback_set pauses it), preempted "
                                        + "(the author touched the editor mid-capture; the playhead stays where "
                                        + "they put it), busy (a save or export), prefab_mode.";

        private readonly BridgeClient _bridge;

        public CaptureTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "view_capture", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("A PNG of the real game picture at a frame, through the level camera with effects and "
                     + "post-processing, editor overlays hidden. It MOVES the author's playhead for a moment "
                     + "and, by the author's setting, puts it back or leaves it there; playhead in the result "
                     + "is where it ended. With region it frames that world rect instead, for a close look at "
                     + "detail the image limit would lose (nothing in the level changes). Prefer view_schematic "
                     + "for layout checks. " + Refusals)]
        public Task<CallToolResult> ViewCapture(
            [Description(FrameDoc)] JsonElement frame,
            [Description("Longest side in pixels, capped by the game's image limit.")] int? size = null,
            [Description("World rect {x, y, width, height} to frame, x/y its lower-left corner; absent for the "
                         + "level camera's view. The image takes its aspect.")] JsonElement? region = null,
            [Description("Id of a shape that carries a reference picture (an imported texture on a quad, kept "
                         + "inactive): the result scores the capture against it (compareMean, compareOver, "
                         + "comparePixels) and tints the pixels that differ magenta.")] int? compare = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.ViewCapture,
                new ViewCaptureRequest
                {
                    Frame = BridgeTool.Time(frame), Size = size,
                    Region = BridgeTool.Time(region)?.ToObject<AgentRegion>(), Compare = compare,
                }, token);

        [McpServerTool(Name = "view_filmstrip", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Several real frames as one labelled contact sheet (f<frame> under each cell): either "
                     + "frames, or from/to with count spread evenly, ends included (default 6, max 16). "
                     + "Captures one after another through the playhead like view_capture; one interruption "
                     + "aborts the whole strip. " + Refusals)]
        public Task<CallToolResult> ViewFilmstrip(
            [Description("First frame of the range. " + FrameDoc)] JsonElement? from = null,
            [Description("End of the range: a frame is the last one; an instant (\"s:3\", \"bar:5\") ends the "
                         + "range, so the last frame is the one before. " + FrameDoc)] JsonElement? to = null,
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

        [McpServerTool(Name = "playhead_set", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Moves the author's playhead to a frame, paused, to show the author something. Only "
                     + "while the author is idle: refused with user_busy otherwise (playback counts as busy; "
                     + "playback_set pauses it).")]
        public Task<CallToolResult> PlayheadSet(
            [Description(FrameDoc)] JsonElement frame,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PlayheadSet,
                new PlayheadSetRequest { Frame = BridgeTool.Time(frame) }, token);

        [McpServerTool(Name = "playback_set", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Plays or pauses the editor, as the author's play button does, optionally from a frame. "
                     + "A level the author opens starts playing (one level_open or level_create opens stays "
                     + "paused), and playback makes the author busy, so "
                     + "every capture, playhead move and music_play is refused until it is paused: pause first. "
                     + "Do not pause playback the author is watching on purpose; ask. Refused only while the "
                     + "author's hands are on the editor (user_busy: drag, scrub, typing) or a save runs (busy). "
                     + "Returns the playhead.")]
        public Task<CallToolResult> PlaybackSet(
            [Description("True to play, false to pause.")] bool playing,
            [Description("Where to start or pause; absent to stay. " + FrameDoc)] JsonElement? frame = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.PlaybackSet,
                new PlaybackSetRequest { Playing = playing, Frame = BridgeTool.Time(frame) }, token);
    }
}
