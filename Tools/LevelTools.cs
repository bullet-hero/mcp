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

    /// <summary> Listing, opening, creating and saving levels. </summary>
    [McpServerToolType]
    public sealed class LevelTools
    {
        private readonly BridgeClient _bridge;

        public LevelTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "level_list", ReadOnly = true)]
        [Description("Levels in the editor's list, one line each: id, name, whether you may open it (writable). "
                     + "Paged: total/offset/next in the header; filter by a name fragment.")]
        public Task<CallToolResult> LevelList(
            [Description("Name fragment, case-insensitive.")] string filter = null,
            [Description("Lines to skip.")] int? offset = null,
            [Description("Lines to return, default 100, max 500.")] int? limit = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelList,
                new LevelListRequest { Filter = filter, Offset = offset, Limit = limit }, token);

        [McpServerTool(Name = "level_open")]
        [Description("Opens a writable level by id (from level_list) in the editor, replacing the open one. "
                     + "Refused: unsaved_changes (the author has unsaved edits: ask them or level_save first), "
                     + "forbidden (read-only source), protected (needs a passphrase), newer_generation. "
                     + "Revision and session id change.")]
        public Task<CallToolResult> LevelOpen(
            [Description("Level id exactly as level_list prints it.")] string id,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelOpen, new LevelOpenRequest { Id = id }, token);

        [McpServerTool(Name = "level_create")]
        [Description("Creates a new empty level and opens it. Refused with unsaved_changes while the open level "
                     + "has unsaved edits. It never writes over an existing level.")]
        public Task<CallToolResult> LevelCreate(
            [Description("Level name.")] string name,
            [Description("Short description.")] string description = null,
            [Description("Frames per second, default 60.")] int? framerate = null,
            [Description("Length: \"s:180\", \"f:600\" or a frame count; default 600 frames.")]
            JsonElement? duration = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelCreate, new LevelCreateRequest
            {
                Name = name,
                Description = description,
                Framerate = framerate,
                Duration = BridgeTool.Time(duration),
            }, token);

        [McpServerTool(Name = "level_save")]
        [Description("Saves the OPEN level in place, never another file. Refused with busy while a save or "
                     + "export is running.")]
        public Task<CallToolResult> LevelSave(CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.LevelSave, null, token);
    }
}
