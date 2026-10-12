using System.Collections.Generic;
using System.ComponentModel;
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

    /// <summary> The level's music: analysis, per-bar tables, a strip picture, musical addresses, grid checks,
    /// playback for the author, and the beat grid itself. </summary>
    [McpServerToolType]
    public sealed class MusicTools
    {
        private const string TimeDoc = "Frame from 1, \"s:12.5\", \"bar:17\", \"bar:17.3\", \"beat:64.5\" or "
                                       + "\"marker:drop+2bars\".";
        private const string ResourceDoc = "Audio resource id (resources_list audios); absent for the level's "
                                           + "first audio track.";

        private readonly BridgeClient _bridge;

        public MusicTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "music_analyze", ReadOnly = true)]
        [Description("Analyses the track (cached after the first call, which can take a few seconds): tempo as "
                     + "the level plays it, phase and downbeat as frames, confidence, ambiguity (half/double "
                     + "tempo risk), drift, sections with similarity letters (A, B, A') and energy L/M/H, onset "
                     + "counts per band, and notes on what to doubt. You cannot hear: trust the level's beat "
                     + "grid over this estimate, and ask the author (music_play) when timing matters.")]
        public Task<CallToolResult> MusicAnalyze(
            [Description(ResourceDoc)] int? resource = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.MusicAnalyze,
                new MusicAnalyzeRequest { Resource = resource }, token);

        [McpServerTool(Name = "music_table", ReadOnly = true)]
        [Description("One row per bar or beat: bar 17 | f 1840 | L7 M5 H3 | low 4 mid 2 high 8 | peak f 1843 "
                     + "(band energy 0-9, onset counts per band, the strongest onset's frame). Rows follow the "
                     + "level's beat grid, else the analysed tempo (grid says which). Paged.")]
        public Task<CallToolResult> MusicTable(
            [Description("First row's time; the start when absent. " + TimeDoc)] JsonElement? from = null,
            [Description("Last row's time; the end when absent. " + TimeDoc)] JsonElement? to = null,
            [Description("bar (default) or beat.")] string by = null,
            [Description(ResourceDoc)] int? resource = null,
            [Description("Rows to skip.")] int? offset = null,
            [Description("Rows to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.MusicTable, new MusicTableRequest
            {
                From = BridgeTool.Time(from), To = BridgeTool.Time(to), By = by, Resource = resource,
                Offset = offset, Limit = limit,
            }, token);

        [McpServerTool(Name = "music_strip", ReadOnly = true)]
        [Description("A PNG of a time range: low/mid/high band energy as heat rows, bar numbers, bar lines and "
                     + "beat ticks, cyan marker ticks, red ticks where objects with a collider start. Use it to "
                     + "see whether hazards land on the music.")]
        public Task<CallToolResult> MusicStrip(
            [Description("Range start; the track start when absent. " + TimeDoc)] JsonElement? from = null,
            [Description("Range end; the track end when absent. " + TimeDoc)] JsonElement? to = null,
            [Description("Width in pixels, capped by the game's image limit; the height is fixed.")]
            int? size = null,
            [Description(ResourceDoc)] int? resource = null,
            CancellationToken token = default)
            => BridgeTool.ForwardImageAsync(_bridge, AgentProtocol.Methods.MusicStrip, new MusicStripRequest
            {
                From = BridgeTool.Time(from), To = BridgeTool.Time(to), Size = size, Resource = resource,
            }, token);

        [McpServerTool(Name = "music_resolve", ReadOnly = true)]
        [Description("Converts a time address to its frame, seconds and bar.beat (when the level's beat grid "
                     + "covers it), e.g. to check what \"bar:17\" or \"marker:drop-1beat\" means.")]
        public Task<CallToolResult> MusicResolve(
            [Description(TimeDoc)] JsonElement time,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.MusicResolve,
                new MusicResolveRequest { Time = BridgeTool.Time(time) }, token);

        [McpServerTool(Name = "music_check", ReadOnly = true)]
        [Description("Objects and keyframes whose frames are off the level's beat grid by more than tolerance, "
                     + "one line each: #2 rect \"x\" start f45: +14 from beat f31 (1.2). Needs a beat grid "
                     + "(beatgrid_apply). Paged.")]
        public Task<CallToolResult> MusicCheck(
            [Description("Allowed distance from a beat in frames, default 1.")] int? tolerance = null,
            [Description("Range start. " + TimeDoc)] JsonElement? from = null,
            [Description("Range end. " + TimeDoc)] JsonElement? to = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.MusicCheck, new MusicCheckRequest
            {
                Tolerance = tolerance, From = BridgeTool.Time(from), To = BridgeTool.Time(to), Offset = offset,
                Limit = limit,
            }, token);

        [McpServerTool(Name = "music_play", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Plays a range in real time for the AUTHOR to listen to (at most 45 s; the call answers "
                     + "when it ends), then puts the playhead back by the author's setting. Use it to ask the "
                     + "author whether timing feels right. Refused: user_busy (playback_set pauses the editor "
                     + "first); refused when the editor's audio is muted (ask the author); preempted when the "
                     + "author touches the editor (playback stops, the playhead stays); busy; prefab_mode.")]
        public Task<CallToolResult> MusicPlay(
            [Description("Range start. " + TimeDoc)] JsonElement from,
            [Description("Range end, inclusive; the level's end (\"s:60\" in a 60 s level) means its last frame. " + TimeDoc)]
            JsonElement to,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.MusicPlay,
                new MusicPlayRequest { From = BridgeTool.Time(from), To = BridgeTool.Time(to) }, token);

        [McpServerTool(Name = "beatgrid_propose", ReadOnly = true)]
        [Description("Beat segments made from the analysis, NOT applied: start, duration, bpm, offset (frames "
                     + "from start to a downbeat), beatsPerBar; notes say what to doubt and which existing "
                     + "segments an apply would cut. Show the author, then beatgrid_apply.")]
        public Task<CallToolResult> BeatgridPropose(
            [Description(ResourceDoc)] int? resource = null,
            [Description("Where the grid starts, a frame or seconds; the track start when absent.")]
            JsonElement? from = null,
            [Description("Its last frame; the end of the track or level when absent.")] JsonElement? to = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.BeatgridPropose, new BeatgridProposeRequest
            {
                Resource = resource, From = BridgeTool.Time(from), To = BridgeTool.Time(to),
            }, token);

        [McpServerTool(Name = "beatgrid_apply", ReadOnly = false, Destructive = true)]
        [Description("Writes beat segments as ONE undo step: existing segments they overlap are trimmed, split "
                     + "or removed first. Result lists every segment afterwards. Refused: stale, user_busy, "
                     + "locked, busy, prefab_mode, refused (the segment limit), invalid_params (overlap among "
                     + "the given segments, a bad field).")]
        public Task<CallToolResult> BeatgridApply(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("Segments as beatgrid_propose returns them: [{start, duration, bpm, offset, "
                         + "beatsPerBar}], start/duration frames or seconds.")] JsonElement segments,
            [Description("What the author's History tab shows.")] string caption = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.BeatgridApply, new BeatgridApplyRequest
            {
                ExpectedRevision = expectedRevision,
                Segments = BridgeTool.Time(segments)?.ToObject<List<BeatgridSegment>>(),
                Caption = caption,
            }, token);
    }
}
