using System.ComponentModel;
using ModelContextProtocol.Server;

namespace BH.MCP.Prompts
{
    // NON-static on purpose: WithPrompts<T>() needs an instance type to survive trimming.

    /// <summary> Short workflow texts a client offers as ready-made requests. </summary>
    [McpServerPromptType]
    public sealed class WorkflowPrompts
    {
        [McpServerPrompt(Name = "build_level_from_music")]
        [Description("Build the open level's content from its music, section by section, with the author.")]
        public string BuildLevelFromMusic(
            [Description("What the author wants: mood, difficulty, ideas. Optional.")] string brief = null)
            => "Build this Bullet Hero level from its music, working with the author who watches the editor.\n"
               + Brief(brief)
               + "1. context_read: the level's brief, decisions and remarks. session_status, level_outline depth 1.\n"
               + "2. music_analyze. If the level has no beat grid: beatgrid_propose, show it to the author, "
               + "music_play a few bars so they can judge it, then beatgrid_apply.\n"
               + "3. music_table by bar and music_strip: propose a section plan (bars, energy, what happens). "
               + "Ask the author before building; record the agreed plan with context_write.\n"
               + "4. Per section: one edit_batch for the skeleton (groups, spans on bar: addresses), then "
               + "batches that fill it. Caption every batch with the section name.\n"
               + "5. After each section: view_schematic at a few bars, frame_stats over it, music_check; "
               + "view_filmstrip for the real look. Fix what reads badly.\n"
               + "6. Ask the author to play it (music_play) and record their remarks with context_write.";

        [McpServerPrompt(Name = "review_section")]
        [Description("Review one stretch of the level against its music and readability, without editing.")]
        public string ReviewSection(
            [Description("Start of the stretch: a frame, \"s:12\", \"bar:17\" or \"marker:drop\".")] string from,
            [Description("End of the stretch, in the same forms.")] string to)
            => $"Review the level from {from} to {to}; change nothing until the author agrees.\n"
               + "1. context_read for what the section is meant to be.\n"
               + "2. music_table and music_strip over the range: where are the hits and the quiet parts?\n"
               + "3. frame_stats over the range; view_schematic at its busiest frames; view_filmstrip of a "
               + "few frames for the real look.\n"
               + "4. music_check over the range for hazards off the beat.\n"
               + "5. Report: what lands on the music, what does not, what is unreadable or unfair, and a short "
               + "list of proposed edits. Record the findings the author accepts with context_write.";

        [McpServerPrompt(Name = "continue_level")]
        [Description("Pick up an unfinished level where the last session left it.")]
        public string ContinueLevel()
            => "Continue work on this Bullet Hero level.\n"
               + "1. context_read: the brief, the section plan, decisions, author remarks, open questions.\n"
               + "2. changes_since a revision you know, or history_get, to see what changed since; "
               + "level_outline depth 1 for the current shape.\n"
               + "3. Find the first section the plan lists as unfinished; view_schematic and music_table there.\n"
               + "4. Ask the author whether the plan still holds, then continue it batch by batch.\n"
               + "5. Before stopping, update the plan and open questions with context_write.";

        [McpServerPrompt(Name = "recreate_picture")]
        [Description("Rebuild a reference picture as level shapes, compared against the picture itself.")]
        public string RecreatePicture(
            [Description("Absolute path of the reference image, with forward slashes: a prompt argument turns "
                         + "a backslash pair such as \\r into a control character.")] string path,
            [Description("What should move, if anything: the motion the author wants. Optional.")]
            string motion = null)
            => $"Rebuild the picture at {path} as shapes of the open level.\n"
               + (string.IsNullOrWhiteSpace(motion) ? "" : "Motion the author wants: " + motion.Trim() + "\n")
               + "1. resource_import it as a texture; edit_batch a quad of its exact extent on a top layer with "
               + "that texture, named as the reference. Compare by view_capture with it on and off (active: "
               + "true/false, never an alpha-0 colour); a region capture zooms in. Keep it, inactive.\n"
               + "2. Catalog shapes first (shapes_find): a Circle, a Square stretched by size and turned by "
               + "rotation for every bar, line and neck. A custom polygon only where none fits, with its real "
               + "corners only and straight edges between; never per-pixel regions. Detail is the price of "
               + "clean edges: a ragged outline or a seam is worse than a lost notch.\n"
               + "3. Light and shade lie ON TOP of one base silhouette, clipped to it so they share its edge; "
               + "never two regions edge to edge, the seam shows. A part another covers runs on under it, so "
               + "the two touch without a gap when either moves.\n"
               + "4. What the picture hides under a moving item is guessed: bridge that stretch of an outline "
               + "straight, or it shows as a bump once the item moves away.\n"
               + "5. Layers are effective: each create layer is the sum up the parent chain. Group what moves "
               + "together; give every moving part its own group.\n"
               + "6. Fill the 16:9 frame: the background reaches every edge. Check the result as a whole and "
               + "zoomed, part by part, against the reference before calling it done; level_save.";

        private static string Brief(string brief)
            => string.IsNullOrWhiteSpace(brief) ? "" : "The author's brief: " + brief.Trim() + "\n";
    }
}
