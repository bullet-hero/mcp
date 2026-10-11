using System.Text.Json;
using System.Text.Json.Serialization;

namespace BH.MCP
{
    // A trimmed binary has no reflection-based JSON: every type a tool parameter can take is listed here.

    /// <summary> Source-generated JSON metadata for the tools' parameter types. </summary>
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(long?))]
    [JsonSerializable(typeof(bool?))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonElement?))]
    internal sealed partial class ToolJsonContext : JsonSerializerContext
    {
    }
}
