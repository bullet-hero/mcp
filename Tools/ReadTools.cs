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

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> Reading the open level: hierarchy, objects, keyframes, events, resources, shapes. </summary>
    [McpServerToolType]
    public sealed class ReadTools
    {
        private const string TimeDoc = "Global frame from 1 (120 or \"f:120\") or seconds (\"s:12.5\").";
        private const string PageDoc = "Pages: header total/offset/next, lines after it. ";

        private readonly BridgeClient _bridge;

        public ReadTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "level_outline", ReadOnly = true)]
        [Description("The object hierarchy, one indented line per object: "
                     + "#12 Shape \"boss-ring\" L5(+2) f120+60 shape:Circle_S2_T2 kids:3 "
                     + "(#id, type, name, L = EFFECTIVE layer with (+n) own layer when different, f = global "
                     + "start frame + duration in frames, kids = children). " + PageDoc
                     + "Start with depth 1-2, then descend with parent.")]
        public Task<CallToolResult> LevelOutline(
            [Description("Object id to list under; absent for the roots.")] int? parent = null,
            [Description("Levels of children to show; absent for all.")] int? depth = null,
            [Description("Only rect, shape, text, effect or prefab.")] string type = null,
            [Description("Name fragment, case-insensitive.")] string name = null,
            [Description("Only objects playing at or after this time. " + TimeDoc)] JsonElement? from = null,
            [Description("Only objects playing at or before this time. " + TimeDoc)] JsonElement? to = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelOutline, new LevelOutlineRequest
            {
                Parent = parent, Depth = depth, Type = type, Name = name, From = BridgeTool.Time(from),
                To = BridgeTool.Time(to), Offset = offset, Limit = limit,
            }, token);

        [McpServerTool(Name = "object_query", ReadOnly = true)]
        [Description("Flat list of objects matching all given filters, in the outline's line format. "
                     + PageDoc + "Use it to find what plays in a time range or what is called something.")]
        public Task<CallToolResult> ObjectQuery(
            [Description("Playing at or after this time. " + TimeDoc)] JsonElement? from = null,
            [Description("Playing at or before this time. " + TimeDoc)] JsonElement? to = null,
            [Description("Name fragment, case-insensitive.")] string name = null,
            [Description("rect, shape, text, effect or prefab.")] string type = null,
            [Description("Only direct children of this object id.")] int? parent = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ObjectQuery, new ObjectQueryRequest
            {
                From = BridgeTool.Time(from), To = BridgeTool.Time(to), Name = name, Type = type, Parent = parent,
                Offset = offset, Limit = limit,
            }, token);

        [McpServerTool(Name = "object_get", ReadOnly = true)]
        [Description("One or a few objects in full as readable JSON (camelCase members, values with a kind), "
                     + "keyframes summarised per track: use keys_get for the keys themselves. Up to 500 ids; "
                     + "ids that do not exist come back in missing.")]
        public Task<CallToolResult> ObjectGet(
            [Description("Object ids, as the outline prints them.")] int[] ids,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ObjectGet,
                new ObjectGetRequest { Ids = ids?.ToList() ?? new List<int>() }, token);

        [McpServerTool(Name = "keys_get", ReadOnly = true)]
        [Description("Keyframes of one object, per track, as JSON: frame (GLOBAL, from 1), ease, value in the "
                     + "form edit_batch key takes. totals gives each track's key count before paging; "
                     + "offset/limit apply per track. Narrow with tracks and from/to.")]
        public Task<CallToolResult> KeysGet(
            [Description("Object id.")] int id,
            [Description("position, rotation, scale, size, anchorMin, anchorMax, pivot, color, uv, textFontSize, "
                         + "textAppearing, textFillment; absent for every track the object has.")]
            string[] tracks = null,
            [Description("Keys at or after this frame. " + TimeDoc)] JsonElement? from = null,
            [Description("Keys at or before this frame. " + TimeDoc)] JsonElement? to = null,
            [Description("Keys to skip per track.")] int? offset = null,
            [Description("Keys to return per track, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.KeysGet, new KeysGetRequest
            {
                Id = id, Tracks = tracks?.ToList(), From = BridgeTool.Time(from), To = BridgeTool.Time(to),
                Offset = offset, Limit = limit,
            }, token);

        [McpServerTool(Name = "levelkeys_get", ReadOnly = true)]
        [Description("Keyframes of the level's own tracks (what edit_batch levelkey writes), as JSON: frame "
                     + "(global), ease, value; and forms: per track, a default value in the exact form levelkey "
                     + "takes, to copy and edit. Owners and tracks: camera position rotation zoom pivot shake; "
                     + "game marker checkpoint screenLimit background theme; player visible control collision "
                     + "size speed; post bloom chromatic vignette lens grain motionBlur colorCurves liftGammaGain "
                     + "shadowsMidtonesHighlights whiteBalance analogGlitch digitalGlitch.")]
        public Task<CallToolResult> LevelKeysGet(
            [Description("camera, game, player or post.")] string owner,
            [Description("Tracks of that owner; absent for all of them.")] string[] tracks = null,
            [Description("Keys at or after this frame. " + TimeDoc)] JsonElement? from = null,
            [Description("Keys at or before this frame. " + TimeDoc)] JsonElement? to = null,
            [Description("Keys to skip per track.")] int? offset = null,
            [Description("Keys to return per track, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelKeysGet, new LevelKeysGetRequest
            {
                Owner = owner, Tracks = tracks?.ToList(), From = BridgeTool.Time(from), To = BridgeTool.Time(to),
                Offset = offset, Limit = limit,
            }, token);

        [McpServerTool(Name = "events_get", ReadOnly = true)]
        [Description("Level-wide events as JSON, one line per event, grouped by kind: markers, beats (beat "
                     + "segments with tempo), checkpoints, audio (tracks). totals gives the count per kind; "
                     + "offset/limit apply per kind.")]
        public Task<CallToolResult> EventsGet(
            [Description("Any of markers, beats, checkpoints, audio; absent for all four.")] string[] kinds = null,
            [Description("Events to skip per kind.")] int? offset = null,
            [Description("Events to return per kind, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.EventsGet,
                new EventsGetRequest { Kinds = kinds?.ToList(), Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "resources_list", ReadOnly = true)]
        [Description("The level's resources of one kind, one line each with id and name. " + PageDoc)]
        public Task<CallToolResult> ResourcesList(
            [Description("textures, fonts, audios, prefabs, themes or effects.")] string kind,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ResourcesList,
                new ResourcesListRequest { Kind = kind, Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "shapes_find", ReadOnly = true)]
        [Description("Finds shapes (built-in catalog and the level's own) by form, sector, thickness or a name "
                     + "fragment; one line each with the name edit_batch create/shape accepts, and the id. "
                     + PageDoc + "With no filter it lists them all, paged.")]
        public Task<CallToolResult> ShapesFind(
            [Description("Geometric form as the catalog names it, e.g. Circle, Square, Hexagon.")] string form = null,
            [Description("Sector as the catalog names it, e.g. Half.")] string sector = null,
            [Description("Thickness: filled, ring, or a ring width T2, T4, T8, T16, T32.")] string thickness = null,
            [Description("Fragment of the name (Circle_S2_T4) or of its description (\"ring 1/4\"), case-insensitive.")]
            string name = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ShapesFind, new ShapesFindRequest
            {
                Form = form, Sector = sector, Thickness = thickness, Name = name, Offset = offset, Limit = limit,
            }, token);
    }
}
