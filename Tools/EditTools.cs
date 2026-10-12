using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> The one write tool. </summary>
    [McpServerToolType]
    public sealed class EditTools
    {
        private readonly BridgeClient _bridge;

        public EditTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "edit_batch", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("The ONLY way to change the level. Runs ops in order as ONE undo step for the author, all or "
                     + "nothing: a failing op rolls everything back and the error names its index. Up to 512 ops; "
                     + "batch one coherent change and caption it. Pass expectedRevision from your last read; on "
                     + "stale re-read (changes_since shows what happened). Times: global frame from 1 (120, "
                     + "\"f:120\") or \"s:12.5\"; with a beat grid also \"bar:17\", \"bar:17.3\" (beat 3 of bar 17), "
                     + "\"beat:64.5\", \"marker:drop\"; any time moved along the grid (\"bar:17+2beats\", "
                     + "\"marker:drop-1bar\") or by frames (\"bar:17-1f\": the last frame before bar 17); a marker an "
                     + "earlier levelkey of the same batch writes is usable below it; durations 60, \"f:60\", \"s:2\", "
                     + "\"2bars\", \"3beats\" and sums (\"1bars+1f\"). A span is half-open, so a key on the frame where "
                     + "it ends is never reached. Inside Prefab Mode only frames and seconds read (from 1 at the "
                     + "template's start). Layers are EFFECTIVE (own plus parents'); a higher layer draws in front, "
                     + "and of two shapes on one layer the NEWER draws BEHIND, so give the one on top a higher "
                     + "layer. Ids are ints; \"$a\" names what an earlier op declared with ref, usable as id, parent, "
                     + "shape, prefab or theme below it; parent -1 is the level's camera (its children move with "
                     + "it).\n"
                     + "ops (every op has op plus):\n"
                     + "create: type rect|shape|text|effect|prefab, ref \"$a\", start, [parent], [name], [layer], "
                     + "[duration], [shape: catalog name, level shape name, id or shapedef ref], [polygon: corners "
                     + "[[x,y],...] in the parent's space (world for a root): defines the outline as a level shape "
                     + "and sets size and position so it draws exactly there; repeats of one outline share one "
                     + "shape], [collider: like shape, \"same\" (what is drawn) or none], [texture: level texture id "
                     + "or file name], [effect: see effect, with [effectName]], [prefab: template guid or a "
                     + "prefab_pack ref of this batch]. A start past the level's last frame is refused. No "
                     + "duration: a root lasts 1 s, cut short (with a warning) to end with the level; a child runs "
                     + "from start to its parent's end, anchored there; a prefab lasts its template's. No layer: "
                     + "the next free one. A shape or text starts with the editor's default size key at its start. "
                     + "A catalog shape keeps its proportions inside the rect (shapes_find says what it draws: a "
                     + "Triangle at size (2,2) is 2 wide, 1.73 tall); a polygon fills its rect\n"
                     + "shapedef: ref \"$s\", points [[x,y],...] (corners in order, 3-130, convex or not, not "
                     + "crossing), [name], [replace: a level shape to rewrite instead]: a level shape that draws "
                     + "and hits at once, fitted to its bounds; draw it at size = the points' width x height. "
                     + "createdShapes gives its id\n"
                     + "shapedelete: [shapes [guid|name|$ref]], [unused: true = every level shape no object draws "
                     + "or collides with]; a shape still in use is refused\n"
                     + "delete: ids [..] (subtrees go too)\n"
                     + "name: id, value\n"
                     + "parent: id, parent (null = root); put it before layer\n"
                     + "layer: id, value\n"
                     + "span: id, [start], [duration], [anchorStart], [anchorEnd] (bool: follow the parent's edge)\n"
                     + "active: id, value (bool)\n"
                     + "shape: id, [shape], [collider], [texture (or none)], [shader auto|opaque|transparent] "
                     + "(shape objects). The collider is what HURTS the player, separate from what is drawn: a "
                     + "shape with no collider is decoration\n"
                     + "effect: id, value: a guid or name from resources_list effects, none, or new (mints the "
                     + "editor's default, a small point burst, named by [effectName], in the same undo step; its "
                     + "settings only through raw_set); an effect object with no effect draws nothing\n"
                     + "text: id, value (text objects; rich text such as <b>bold</b> works)\n"
                     + "textstyle: id, [font: level or game font id or name, resources_list fonts], [align "
                     + "Left|Center|Right], [valign Top|Middle|Bottom], [wrap bool], [mask: the appearing mask "
                     + "characters]\n"
                     + "theme: ref \"$th\", [from: game or level theme to copy, default the game's first], [name], "
                     + "[slots {\"2\":\"#RRGGBBAA\",...}] makes a level theme; or id (theme guid or name), [name], "
                     + "[slots] edits one. Slots are 0-63, the n of {\"theme\":n}: 0 fallback, 1 GUI, 2 background, "
                     + "3-6 players, 7 tail, 16-24 objects, 32-40 parallax, 48-56 effects. Activate it with "
                     + "levelkey game theme (value: guid, name or ref)\n"
                     + "key: id, track, frame (global), value, [ease], [space local|world]; adds the key or "
                     + "overwrites the one there. space world (position only, literal Vector2Value) takes the pivot "
                     + "in world units, as cursor_resolve reports it, and converts it for a child\n"
                     + "unkey: id, track, frame\n"
                     + "meta: field name|description|tags|authors, value (tags and authors are lists; an author is "
                     + "a text or {name, url, credit})\n"
                     + "resourcemeta: kind texture|font|audio, id, [title], [description], [url], [sources] "
                     + "(texts), [authors]: the attribution record of an imported file; given fields replace theirs\n"
                     + "levelkey: owner, track, frame, value, [ease] / levelunkey: owner, track, frame; "
                     + "levelkeys_get shows a track's keys and the value form it takes. A track's first key holds "
                     + "from frame 1 (a warning says so), so key the start too when a change should begin later. "
                     + "camera: position rotation (RADIANS) zoom pivot shake (zoom is the visible HEIGHT in world "
                     + "units, default 10, not a multiplier); game: marker {name,description,color4} checkpoint "
                     + "{name,active,color4,position,space,restoreHealth} screenLimit background (\"#RRGGBB\") theme; "
                     + "player: visible control collision size speed; post: bloom chromatic vignette lens grain "
                     + "motionBlur colorCurves liftGammaGain shadowsMidtonesHighlights whiteBalance analogGlitch "
                     + "digitalGlitch\n"
                     + "audiotrack: ref \"$t\", start, [name, duration, layer 0-15, resource (id, or the file or "
                     + "preset name), offset (s), speed, volume] (a resource with no duration lasts as long as its "
                     + "clip, cut at the level's end with a warning); audioset: id, same optional fields; "
                     + "audiodelete: ids; audiokey: id, track volume|stereoPan, frame, value, [ease]; audiounkey: "
                     + "id, track, frame. events_get audio lists a track's keys\n"
                     + "setting: field framerate|duration (frames)|seed|orientation, value (framerate keeps frame "
                     + "numbers, so everything retimes)\n"
                     + "prefab_pack: ref, ids, [name] (ids become a template plus one placement; create type prefab "
                     + "with prefab: that ref places more copies in the same batch); prefab_flatten: ids, "
                     + "[recursive]. Pack any element that repeats.\n"
                     + "In Prefab Mode the template root is id -3 and level-wide verbs are refused.\n"
                     + "tracks: position, rotation, scale, size, anchorMin, anchorMax, pivot, color, uv, "
                     + "textFontSize, textAppearing, textFillment; rotation is in RADIANS (cursor_resolve reports "
                     + "rotation in radians too, generators take degrees). textFontSize: a number is a fixed size "
                     + "in world units, {minValue, maxValue} a band the text shrinks into to fit its rect. "
                     + "textAppearing {value, mode Forward|Backward|Random} is the share of characters HIDDEN "
                     + "behind the mask (0 all shown, 1 all masked); textFillment {value, direction "
                     + "Forward|Backward|FromCenter|ToCenter} the share written. Values are readable JSON with a "
                     + "kind, e.g. {\"kind\":\"Vector2Value\",\"x\":3,\"y\":4}; a bare number is a plain float (rotation "
                     + "1.57). color: \"#RRGGBBAA\", {\"theme\":2}, or a gradient over the rect: "
                     + "{\"kind\":\"ColorVerticalKey\",\"color4Bottom\":..,\"color4Top\":..}, "
                     + "{\"kind\":\"ColorHorizontalKey\",\"color4Left\":..,\"color4Right\":..}, "
                     + "{\"kind\":\"Color4X4Key\",\"color4BL\":..,\"color4BR\":..,\"color4TL\":..,\"color4TR\":..}. Random "
                     + "values: FloatMinMax, Vector2Rect, Vector2Circle, Vector2Lerp, Color4MinMax, Color4Lerp "
                     + "(keys_get shows their members). Copy the shape of a track's value from keys_get (forms, for "
                     + "a track with no keys). ease is an EaseType name (Linear, Constant, InSine, OutQuad, ...); "
                     + "the ease of the LATER key of a pair shapes the move into it. A key outside its object's "
                     + "span is never reached.\n"
                     + "Example: "
                     + "{\"expectedRevision\":41,\"caption\":\"Roof\",\"ops\":[{\"op\":\"create\",\"ref\":\"$a\",\"type\":\"shape\",\"start\":\"s:2\",\"duration\":\"s:1\",\"name\":\"roof\",\"polygon\":[[-2,0],[2,0],[0,1.5]]},{\"op\":\"key\",\"id\":\"$a\",\"track\":\"color\",\"frame\":\"s:2\",\"value\":\"#E5321EFF\"}]}\n"
                     + "Result: revision, created (ref to id; createdAudio, createdPrefabs, createdThemes for those refs; "
                     + "createdShapes is shape id to its refs, space-separated: equal polygons share one shape), warnings (clamps and legal oddities: a span past the level's "
                     + "end, keys outside a span), hints. Refusals: stale, user_busy, locked, refused (op index and "
                     + "reason), invalid_params.")]
        public Task<CallToolResult> EditBatch(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("What the author's History tab shows for this step, e.g. \"Chorus 1: ring bursts\".")]
            string caption,
            [Description("The ops array (a JSON array of objects), as described by the tool. Give it or opsFile.")]
            JsonElement? ops = null,
            [Description("Instead of ops: the path of a local UTF-8 JSON file holding the ops array (or an object "
                         + "with an \"ops\" array). This server reads it, so a big batch - a traced picture - never "
                         + "travels through the conversation.")]
            string opsFile = null,
            CancellationToken token = default)
        {
            var hasOps = ops.HasValue && ops.Value.ValueKind != JsonValueKind.Null
                                      && ops.Value.ValueKind != JsonValueKind.Undefined;
            List<JObject> list;
            if (!string.IsNullOrWhiteSpace(opsFile))
            {
                if (hasOps)
                    return Task.FromResult(BridgeTool.Error(AgentErrorCode.InvalidParams,
                        "Give ops or opsFile, not both."));
                if (!TryReadOpsFile(opsFile, out list, out var error))
                    return Task.FromResult(BridgeTool.Error(AgentErrorCode.InvalidParams, error));
            }
            else if (hasOps)
            {
                list = BridgeTool.Objects(ops.Value);
            }
            else
            {
                return Task.FromResult(BridgeTool.Error(AgentErrorCode.InvalidParams, "ops or opsFile is required."));
            }

            return BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.EditBatch, new EditBatchRequest
            {
                ExpectedRevision = expectedRevision,
                Caption = caption,
                Ops = list,
            }, token);
        }

        /// <summary> The ops of a batch file: a JSON array, or an object carrying one under <c>ops</c>. </summary>
        public static bool TryReadOpsFile(string path, out List<JObject> ops, out string error)
        {
            ops = null;
            error = null;
            string text;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    error = $"opsFile {path} does not exist.";
                    return false;
                }

                if (info.Length > MaxOpsFileBytes)
                {
                    error = $"opsFile {path} is {info.Length} bytes, over {MaxOpsFileBytes}.";
                    return false;
                }

                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or ArgumentException or NotSupportedException)
            {
                error = $"opsFile {path} cannot be read: {exception.Message}";
                return false;
            }

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch (JsonReaderException exception)
            {
                error = $"opsFile {path} is not JSON: {exception.Message}";
                return false;
            }

            if (token is JObject wrapper) token = wrapper["ops"];
            if (!(token is JArray array))
            {
                error = $"opsFile {path} holds no ops array.";
                return false;
            }

            ops = new List<JObject>(array.Count);
            foreach (var item in array) ops.Add(item as JObject);
            return true;
        }

        /// <summary> The largest batch file read, bytes. </summary>
        public const int MaxOpsFileBytes = 8 * 1024 * 1024;
    }
}
