using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BH.MCP.Tools
{
    // The one place a bridge answer becomes an MCP result, so every tool reports a refusal the same
    // way: IsError with a JSON body naming the code, which is what the AI's retry rules key on.

    /// <summary> Forwards a tool call to the game and shapes the answer. </summary>
    public static class BridgeTool
    {
        /// <summary> Calls <paramref name="method"/>; returns its result, or the refusal, as text. </summary>
        public static async Task<CallToolResult> ForwardAsync(BridgeClient bridge, string method, object parameters,
            CancellationToken token)
        {
            bridge.Hints.BeginCall();

            try
            {
                var result = await bridge.CallAsync(method, parameters, token).ConfigureAwait(false);
                return Text(Shape(result, bridge.Hints), false);
            }
            catch (BridgeException e)
            {
                return Refusal(e);
            }
        }

        private static CallToolResult Refusal(BridgeException e)
        {
            var error = new JObject
            {
                ["error"] = e.Code,
                ["message"] = e.Message,
            };

            if (e.ErrorData != null) error["data"] = JObject.FromObject(e.ErrorData);
            return Text(error.ToString(Formatting.None), true);
        }

        /// <summary> Like <see cref="ForwardAsync"/> for a result carrying a picture: the PNG becomes an image
        /// block, the rest of the result the text block after it. </summary>
        public static async Task<CallToolResult> ForwardImageAsync(BridgeClient bridge, string method,
            object parameters, CancellationToken token)
        {
            bridge.Hints.BeginCall();

            try
            {
                var result = await bridge.CallAsync(method, parameters, token).ConfigureAwait(false);
                return Image(result, bridge.Hints);
            }
            catch (BridgeException e)
            {
                return Refusal(e);
            }
        }

        /// <summary> Splits a picture result into an image block and the text of everything else. </summary>
        public static CallToolResult Image(JToken result, HintFilter hints)
        {
            var png = (result as JObject)?["png"]?.Value<string>();
            if (string.IsNullOrEmpty(png)) return Text(Shape(result, hints), false);

            var obj = (JObject)result;
            obj.Remove("png");

            return new CallToolResult
            {
                Content =
                {
                    ImageContentBlock.FromBytes(Convert.FromBase64String(png), "image/png"),
                    new TextContentBlock { Text = Shape(obj, hints) },
                },
            };
        }

        /// <summary> A page as a header line and its lines; anything else as compact JSON. Hints are
        /// filtered through <paramref name="hints"/> and come last. </summary>
        public static string Shape(JToken result, HintFilter hints)
        {
            if (!(result is JObject obj)) return result.ToString(Formatting.None);

            var due = hints.Due(obj["hints"] as JArray);
            obj.Remove("hints");

            if (!(obj["lines"] is JArray lines) || obj["total"] == null)
            {
                if (due.Count > 0) obj["hints"] = new JArray(due);
                return obj.ToString(Formatting.None);
            }

            var total = obj["total"].Value<int>();
            var offset = obj["offset"]?.Value<int>() ?? 0;
            var text = new StringBuilder();
            text.Append("total=").Append(total).Append(" offset=").Append(offset)
                .Append(" revision=").Append(obj["revision"]?.Value<int>() ?? 0)
                .Append(" scope=").Append(obj["scope"]?.Value<string>() ?? "level");

            if (offset + lines.Count < total) text.Append(" next=").Append(offset + lines.Count);

            foreach (var line in lines) text.Append('\n').Append(line.Value<string>());
            foreach (var hint in due) text.Append("\nhint: ").Append(hint);
            return text.ToString();
        }

        /// <summary> A time or duration argument as the JSON the game parses, or null when absent. </summary>
        public static JToken Time(JsonElement? value)
        {
            if (value == null || value.Value.ValueKind == JsonValueKind.Null
                              || value.Value.ValueKind == JsonValueKind.Undefined)
                return null;

            return JToken.Parse(value.Value.GetRawText());
        }

        /// <summary> A list of ops, as the objects the game reads. </summary>
        public static List<JObject> Objects(JsonElement array)
        {
            var list = new List<JObject>();
            if (array.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in array.EnumerateArray())
                list.Add(item.ValueKind == JsonValueKind.Object ? JObject.Parse(item.GetRawText()) : null);

            return list;
        }

        private static CallToolResult Text(string text, bool isError) => new CallToolResult
        {
            IsError = isError,
            Content = { new TextContentBlock { Text = text } },
        };
    }
}
