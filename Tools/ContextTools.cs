using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BH.MCP.Tools
{
    // NON-static on purpose: WithTools<T>() needs an instance type to survive trimming.

    /// <summary> The level's ai/ folder (memory across sessions) and the guide rendered from the public docs.
    /// </summary>
    [McpServerToolType]
    public sealed class ContextTools
    {
        private readonly BridgeClient _bridge;

        public ContextTools(BridgeClient bridge) => _bridge = bridge;

        [McpServerTool(Name = "context_list", ReadOnly = true)]
        [Description("The open level's ai/ folder: files with sizes, whether it is shared with the level "
                     + "(the author's choice), whether you may write, and the size limits.")]
        public Task<CallToolResult> ContextList(CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ContextList, null, token);

        [McpServerTool(Name = "context_read", ReadOnly = true)]
        [Description("Reads one file of the level's ai/ folder, CONTEXT.md by default: the brief, music notes, "
                     + "section plan, decisions, author remarks and open questions earlier sessions recorded. "
                     + "Read it first. Missing is not an error: content is empty and you may create it.")]
        public Task<CallToolResult> ContextRead(
            [Description("File name in ai/, e.g. CONTEXT.md or plan.json; absent for CONTEXT.md.")]
            string file = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ContextRead,
                new ContextReadRequest { File = file }, token);

        [McpServerTool(Name = "context_write", ReadOnly = false, Destructive = true)]
        [Description("Records what a future session needs in the level's ai/ folder: either content (replaces "
                     + "the file) or heading + text (appends text under that heading of a .md file, adding the "
                     + "heading when missing). CONTEXT.md sections: Brief, Music, Sections, Decisions, Author "
                     + "remarks, Open questions. A file write, NOT an undo step. Names: lower-case a-z 0-9 - _ . "
                     + "with .md or .json (CONTEXT.md is the one upper-case name). Refused: forbidden (a level "
                     + "you may not write, manifest.json), invalid_params, refused (folder full), busy, locked.")]
        public Task<CallToolResult> ContextWrite(
            [Description("File name in ai/; absent for CONTEXT.md.")] string file = null,
            [Description("The whole new file text; leave out when appending.")] string content = null,
            [Description("Heading to append under, e.g. Decisions; give with text instead of content.")]
            string heading = null,
            [Description("Text to append under heading.")] string text = null,
            CancellationToken token = default)
            => BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.ContextWrite, new ContextWriteRequest
            {
                File = file,
                Content = content,
                AppendSection = heading == null && text == null
                    ? null
                    : new ContextAppendSection { Heading = heading, Text = text },
            }, token);

        [McpServerTool(Name = "guide_get", ReadOnly = true)]
        [Description("A topic of the Bullet Hero guide, rendered from the public docs: index (the list), "
                     + "coordinates, time, randomness, prefabs, shapes, avatar, generators, design, pictures, "
                     + "context (the CONTEXT.md skeleton), mcp; and edit, the edit_batch reference, which the "
                     + "server answers itself (no game needed).")]
        public Task<CallToolResult> GuideGet(
            [Description("Topic name; absent for index.")] string topic = null,
            CancellationToken token = default)
            => topic == EditGuide.Topic
                ? Task.FromResult(BridgeTool.Text(EditGuide.Text, false))
                : BridgeTool.ForwardAsync(_bridge, AgentProtocol.Methods.GuideGet,
                    new GuideGetRequest { Topic = topic }, token);
    }
}
