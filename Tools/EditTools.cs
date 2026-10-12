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
                     + "\"f:120\") or \"s:12.5\"; with a beat grid also \"bar:17\", \"bar:17.3\", \"beat:64.5\", "
                     + "\"marker:drop+2bars\"; durations 60, \"f:60\", \"s:2\", \"2bars\", \"3beats\". Layers are "
                     + "EFFECTIVE (own plus "
                     + "parents'). Ids are ints; \"$a\" names an object an earlier create declared with ref, usable "
                     + "as id or parent below it.\n"
                     + "ops (every op has op plus):\n"
                     + "create: type rect|shape|text|effect|prefab, ref \"$a\", start, [parent], [name], [layer], "
                     + "[duration], [shape: catalog name or id, see shapes_find], [collider: the same, or none], "
                     + "[effect: see effect], [prefab: template guid]. A start past the level's last frame is "
                     + "refused. No duration: a root lasts 1 s, cut short (with a warning) to end with the "
                     + "level; a child runs from start to its parent's end, anchored there; a prefab lasts its "
                     + "template's. No layer: the next free one. A shape or text starts with the editor's default "
                     + "size key at its start (keys_get shows it)\n"
                     + "delete: ids [..] (subtrees go too)\n"
                     + "name: id, value\n"
                     + "parent: id, parent (null = root); put it before layer\n"
                     + "layer: id, value\n"
                     + "span: id, [start], [duration], [anchorStart], [anchorEnd] (bool: follow the parent's edge)\n"
                     + "active: id, value (bool)\n"
                     + "shape: id, [shape], [collider], [shader auto|opaque|transparent] (shape objects). The "
                     + "collider is what HURTS the player, separate from what is drawn: a shape with no collider "
                     + "is decoration; \"none\" removes it\n"
                     + "effect: id, value: a guid or name from resources_list effects, none, or new (mints a "
                     + "default effect, named by [effectName], in the same undo step); an effect object with no "
                     + "effect draws nothing\n"
                     + "text: id, value (text objects)\n"
                     + "key: id, track, frame (global), value, [ease], [space local|world]; adds the key or "
                     + "overwrites the one there. space world (position only, literal Vector2Value) takes the "
                     + "pivot in world units, as cursor_resolve reports it, and converts it for a child\n"
                     + "unkey: id, track, frame\n"
                     + "meta: field name|description|tags|authors, value\n"
                     + "levelkey: owner, track, frame, value, [ease] / levelunkey: owner, track, frame; "
                     + "levelkeys_get shows a track's keys and the value form it takes. camera: position rotation "
                     + "zoom pivot shake (zoom is the visible HEIGHT in world units, default 10, not a "
                     + "multiplier); game: marker {name,description,color4} checkpoint "
                     + "{name,active,color4,position,space,restoreHealth} screenLimit background theme; player: "
                     + "visible control collision size speed; post: bloom chromatic vignette lens grain "
                     + "motionBlur colorCurves liftGammaGain shadowsMidtonesHighlights whiteBalance "
                     + "analogGlitch digitalGlitch\n"
                     + "audiotrack: ref \"$t\", start, [name, duration, layer 0-15, resource, offset (s), speed, "
                     + "volume] (a resource with no duration lasts as long as its clip); audioset: id, same "
                     + "optional fields; audiodelete: ids; audiokey: id, track "
                     + "volume|stereoPan, frame, value, [ease]; audiounkey: id, track, frame\n"
                     + "setting: field framerate|duration (frames)|seed|orientation, value (framerate keeps "
                     + "frame numbers, so everything retimes)\n"
                     + "prefab_pack: ref, ids, [name] (ids become a template plus one placement); "
                     + "prefab_flatten: ids, [recursive]\n"
                     + "In Prefab Mode the template root is id -3 and level-wide verbs are refused.\n"
                     + "tracks: position, rotation, scale, size, anchorMin, anchorMax, pivot, color, uv, "
                     + "textFontSize, textAppearing, textFillment; rotation is in RADIANS. Values are readable "
                     + "JSON with a kind, e.g. "
                     + "{\"kind\":\"Vector2Value\",\"x\":3,\"y\":4}; a bare number is a plain float (rotation "
                     + "1.57); colors \"#RRGGBBAA\" or {\"theme\":2}; copy the shape of a track's value from "
                     + "keys_get (forms, for a track with no keys). ease is an EaseType name (Linear, Constant, "
                     + "InSine, OutQuad, ...); the ease of the LATER key of a pair shapes the move into it. A key "
                     + "outside its object's span is never reached.\n"
                     + "Example: {\"expectedRevision\":41,\"caption\":\"Ring\",\"ops\":[{\"op\":\"create\","
                     + "\"ref\":\"$a\",\"type\":\"shape\",\"start\":\"s:2\",\"duration\":\"s:1\",\"name\":\"ring\","
                     + "\"shape\":\"Circle_S2_T2\"},{\"op\":\"key\",\"id\":\"$a\",\"track\":\"size\","
                     + "\"frame\":\"s:2\",\"value\":{\"kind\":\"Vector2Value\",\"x\":4,\"y\":4}}]}\n"
                     + "Result: revision, created (ref to id; createdAudio, createdPrefabs for those refs), "
                     + "warnings (clamps and legal oddities: a span past the level's end, keys outside a span), "
                     + "hints. "
                     + "Refusals: stale, user_busy, locked, refused (op index and reason), "
                     + "invalid_params.")]
        public Task<CallToolResult> EditBatch(
            [Description("Revision from your last read; a mismatch is stale.")] int expectedRevision,
            [Description("What the author's History tab shows for this step, e.g. \"Chorus 1: ring bursts\".")]
            string caption,
            [Description("The ops array (a JSON array of objects), as described by the tool.")] JsonElement ops,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.EditBatch, new EditBatchRequest
            {
                ExpectedRevision = expectedRevision,
                Caption = caption,
                Ops = BridgeTool.Objects(ops),
            }, token);
    }
}
