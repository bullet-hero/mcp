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
        [Description("The ONLY way to change the level. Runs ops in order as ONE undo step for the "
                     + "author, all or nothing: a failing op rolls everything back and the error names its "
                     + "index. Up to 512 ops; batch one coherent change and caption it. Pass "
                     + "expectedRevision from your last read; on stale re-read (changes_since shows what "
                     + "happened). Read guide_get topic edit once before building: times, value forms, "
                     + "colours, tracks, themes, level keys, audio. Times: a global frame from 1 or "
                     + "\"s:12.5\"; with a beat grid \"bar:17\", \"beat:64.5\", \"marker:drop\". Layers are "
                     + "EFFECTIVE (own plus parents'); a higher layer draws in front, and two objects never "
                     + "share a layer while both play: the batch lifts the later one and warns. Ids are "
                     + "ints; \"$a\" names what an earlier op declared with ref, usable as id, parent, "
                     + "shape, prefab or theme below it; parent -1 is the level's camera.\n"
                     + "Build with prefabs: anything that appears more than once is one prefab_pack and its "
                     + "placements (create type prefab), each keying only what differs (an override). "
                     + "Prefer catalog shapes (Circle and Square stretch: a turned Square is any bar or "
                     + "line); a polygon only where none fits, with its real corners only.\n"
                     + "ops (every op has op plus):\n"
                     + "create: type rect|shape|text|effect|prefab, ref \"$a\", start, [parent], [name], "
                     + "[layer], [duration], [shape: catalog name, level shape name, id or shapedef ref], "
                     + "[polygon: corners [[x,y],...] in the parent's space (world for a root), drawn "
                     + "exactly there; repeats share one shape], [collider: like shape, \"same\" or none], "
                     + "[texture: level texture id or file name], [effect, effectName], [prefab: template "
                     + "guid or a prefab_pack ref of this batch]. No duration: a root lasts 1 s, a child "
                     + "runs to its parent's end, a prefab lasts its template's. No layer: the next free "
                     + "one\n"
                     + "shapedef: ref \"$s\", points (3-130 corners in order, not crossing), [name], "
                     + "[replace]: a level shape, drawn at size = the points' width x height\n"
                     + "shapedelete: [shapes [guid|name|$ref]], [unused: true]; a shape in use is refused\n"
                     + "delete: ids (subtrees go too) / name: id, value / parent: id, parent (null = root), "
                     + "before layer / layer: id, value\n"
                     + "span: id, [start], [duration], [anchorStart], [anchorEnd]\n"
                     + "active: id, value (bool); an inactive object stays but draws nothing: hide with it, "
                     + "not with an alpha-0 colour\n"
                     + "shape: id, [shape], [collider], [texture (or none)], [shader "
                     + "auto|opaque|transparent]. Only the collider HURTS the player; a shape with no "
                     + "collider is decoration\n"
                     + "effect: id, value (resources_list effects, none, or new) / text: id, value (rich "
                     + "text works) / textstyle: id, [font], [align], [valign], [wrap], [mask]\n"
                     + "key: id, track, frame (global), value, [ease], [space local|world] / unkey: id, "
                     + "track, frame\n"
                     + "theme, levelkey, levelunkey, audiotrack, audioset, audiodelete, audiokey, "
                     + "audiounkey, meta, resourcemeta, setting: see guide_get topic edit\n"
                     + "prefab_pack: ref, ids, [name] (a template plus one placement) / prefab_flatten: "
                     + "ids, [recursive]. In Prefab Mode the template root is id -3 and level-wide verbs "
                     + "are refused.\n"
                     + "Example: "
                     + "{\"expectedRevision\":41,\"caption\":\"Roof\",\"ops\":[{\"op\":\"create\",\"ref\":\"$a\",\"type\":\"shape\",\"start\":\"s:2\",\"duration\":\"s:1\",\"name\":\"roof\",\"polygon\":[[-2,0],[2,0],[0,1.5]]},{\"op\":\"key\",\"id\":\"$a\",\"track\":\"color\",\"frame\":\"s:2\",\"value\":\"#E5321EFF\"}]}\n"
                     + "Result: revision, created (ref to id; createdAudio, createdPrefabs, createdThemes; "
                     + "createdShapes is shape id to its refs), warnings (clamps, lifted layers and legal "
                     + "oddities), hints. Refusals: stale, user_busy, locked, refused (op index and "
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
