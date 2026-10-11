# Bullet Hero MCP

The MCP server of [Bullet Hero](https://bullethero.space/): it lets an AI assistant work in the game's
level editor beside the author. User documentation: the "AI assistant" page of the editor docs on
https://bullethero.space/.

A thin MCP server (stdio) that forwards tool calls to the running game's bridge: a loopback TCP
listener inside the game, one JSON-RPC 2.0 request per line. It reads where the game listens from the
endpoint file `<persistentDataPath>/mcp/endpoint.json`, which the game writes while MCP is enabled in
Settings. All editor logic lives in the game; this process only connects, performs the `hello`
handshake with the file's token, forwards, and formats the answer.

The wire contract (method names, error codes, limits) is `Agent/` in the
[SDK](https://github.com/bullet-hero/sdk) (`AgentProtocol`, `AgentErrorCode`, `AgentRules`). The game
refuses a server whose `AgentProtocol.Version` differs from its own, so a mismatch fails at `hello`
with `protocol_mismatch` instead of misbehaving later.

## Which SDK it compiles against

| Where the repository sits | SDK used |
|---|---|
| on its own (a plain clone) | the `BH.SDK` submodule here: `git clone --recursive`, or `git submodule update --init` |
| inside a host repository (the game) | the host's own SDK: a `BH.MCP.host.props` in a folder above this repository sets `BhSdkDir` |

The host file is three lines:

```xml
<Project>
  <PropertyGroup><BhSdkDir>$(MSBuildThisFileDirectory)Assets/Plugins/BH.SDK/</BhSdkDir></PropertyGroup>
</Project>
```

A build prints `BH.MCP compiles against <path>`, and fails with a clear message when that SDK is
missing. Inside a host the `BH.SDK` submodule here stays uninitialized and unused; a game build always
ships a server compiled against the protocol it ships itself.

## Build and test

```sh
dotnet build BH.MCP.csproj
dotnet test  Tests/BH.MCP.Tests.csproj
```

The tests run a `FakeBridge` (a real loopback socket speaking the same protocol), so they need no game.
Any .NET 8 SDK works, including the one bundled with Unity: `<Unity>/Editor/Data/DotNetSdk/dotnet`.

## Publish a self-contained binary

```sh
dotnet publish BH.MCP.csproj -c Release -r win-x64 -o bin/publish/win-x64
```

Giving `-r` is what makes it self-contained, single-file and trimmed: the csproj sets those itself.
Never pass them as `-p:` flags - a command-line property is global, reaches the netstandard SDK through
the ProjectReference, and fails there (NETSDK1124). Other runtime identifiers (`linux-x64`,
`osx-arm64`, `osx-x64`) cross-publish the same way. The binary is `mcp.exe`
(`mcp` off Windows). A game build publishes the server itself, with the same command, and
puts the binary beside the game's executable.

## Versions and releases

Three numbers, each with one owner:

| Number | Owner | Means |
|---|---|---|
| `pv`, the protocol version as released (e.g. 1.2.0) | `<Version>` in `BH.MCP.csproj` | this program and the protocol it speaks; `serverInfo.version`, the binary's file version, the release tag |
| the wire number (`AgentProtocol.Version`, an integer) | the SDK's `Agent/` | compatibility only: the game refuses a `hello` whose number differs from its own. Not raised before the first release (pv 1.3.0); after it, every breaking wire change raises it |
| the SDK (`sv`, `SdkVersion.Value`) | the SDK | the library compiled in; shown in `serverInfo.description` beside the protocol |

A release is a tag `pv<X.Y.Z>` equal to `<Version>`, on a commit whose `CHANGELOG.md` has a
`## pv X.Y.Z` section. `.github/workflows/release.yml` then tests, publishes every runtime identifier
on its own OS (`bullet-hero-mcp-<version>-<rid>.zip` / `.tar.gz`) and creates the GitHub Release. Run
it by hand from the Actions tab to build everything without releasing: the archives stay on the run.

## Icon

`Icon/icon.png` is the source. `Icon/icon.ico` is made from it (sizes 16 to 256) and is the Windows
binary's icon; `Icon/icon-64.png` is embedded and sent inline as the MCP `serverInfo` icon. Change
the PNG, then remake both.

It is the game's face with the colours inverted - dark body, light blue eyes - because the binary sits
beside the game's executable: at 16 px a badge or a label is unreadable, so only colour tells a player
which one to start.

## Connect a client

Any MCP client that launches a stdio server takes the same shape:

```json
{ "mcpServers": { "bullet-hero": { "command": "<path to mcp(.exe)>", "args": ["--endpoint", "<path to endpoint.json>"] } } }
```

`--endpoint` is optional: without it the server looks in the game's default data folder for this
machine (`AgentEndpoint.DefaultPath`). The game's Settings screen has a Copy client config button that
fills both paths in. `--game-exe <path>` is optional too: it names the game `app_launch` starts, which
otherwise is the installed game beside the server (none for a server in `bin/`).

## Logs

A stdio server belongs to the client that started it: its stderr goes to that client, and a second copy
started by hand is a different process that nobody talks to. So every server also appends to one shared
file beside the endpoint file, `<persistentDataPath>/mcp/logs/server.log` (`ServerLog.PathFor`), each line
carrying its process id. A server starting on a file over `ServerLog.MaxBytes` moves it to `server.1.log`.

- **One line per tool call** (`BridgeTool.CallLine`): `edit_batch 12 ms ok`, or the refusal's code and
  message. A refusal the AI is meant to handle is `info`; `internal` and `not_connected` are `warn`.
- **`--log-level <Trace|Debug|Information|Warning|Error>`**, `Information` by default. `Debug` adds each
  call's parameters and the MCP SDK's own request lines, which are held at `Warning` otherwise.
- **Started by hand, the server is a log viewer** (`LogViewer`): a console whose stdin is not redirected
  (a double click) prints what the program is, the client config and the log path, then follows the
  file. A client always pipes stdin. `--stdio` forces the server, `--logs` the viewer.

## Tools

Class per area under `Tools/`; the name is the bridge method with `_` for `.`.

| Class | Tool | Does |
|---|---|---|
| `SessionTools` | `session_status` | connection, screen, open level, revision, author busy, AI rights |
| `AppTools` | `app_navigate` | move the running game to a screen with a launch-argument line |
| | `app_launch` | start the game when it is not running (server-side, no bridge method) |
| `LevelTools` | `level_list` | levels in the editor's list (paged) |
| | `level_open` | open a writable level by id |
| | `level_create` | new empty level (name, framerate, duration) |
| | `level_save` | save the open level |
| `ReadTools` | `level_outline` | hierarchy as indented lines (paged, depth, filters) |
| | `object_query` | objects by time range, name, type or parent (paged) |
| | `object_get` | objects in full as readable JSON |
| | `keys_get` | keyframes of chosen tracks, global frames |
| | `events_get` | markers, beat segments, checkpoints, audio tracks |
| | `resources_list` | textures, fonts, audios, prefabs, themes, effects |
| | `shapes_find` | shapes by form, sector, thickness or name |
| `EditTools` | `edit_batch` | the one write tool: ops in one undo step, verb vocabulary in its description |
| `HistoryTools` | `history_get` | the undo history, AI batches captioned |
| | `history_undo` / `history_redo` | step the history |
| | `changes_since` | what changed after a revision, and who |
| `SelectionTools` | `selection_get` / `selection_set` | the author's selection |
| `PrefabTools` | `prefab_mode_enter` / `prefab_mode_exit` | Prefab Mode: reads and writes move into a template |
| | `prefab_overrides` | what a placement changes against its template |
| `GeneratorTools` | `generator_list` / `generator_schema` | scope generators and their parameter schema |
| | `generator_run` | one generator over a time window, one undo step |
| `MaintenanceTools` | `resource_import` | copy a texture, font or audio file into the level, its own undo step |
| | `raw_get` / `raw_set` | the level file as a tree; unvalidated write, last resort |
| | `command_list` / `command_run` | whitelisted editor commands |
| | `validate_list` / `validate_fix` | validation findings; automatic fixes in one undo step |
| `VisionTools` | `view_schematic` | PNG schematic of any frame, computed off screen (no playhead) |
| | `frame_stats` | per-frame counts and hazard coverage over a range |
| | `cursor_resolve` | where objects are at a frame: world rect, pivot, effective layer, resolved span |
| | `object_explain` | why an object is or is not drawn at a frame |
| | `shapes_atlas` | PNG contact sheet of shapes, numbered, with their ids |
| `CaptureTools` | `view_capture` / `view_filmstrip` | real pixels through the shared playhead; one frame or a labelled sheet |
| | `playhead_get` / `playhead_set` | the author's playhead (set only while the author is idle) |
| `MusicTools` | `music_analyze` | tempo, downbeat, sections, notes from the track (cached) |
| | `music_table` / `music_strip` | per-bar or per-beat rows; a PNG of band energy, grid, markers, hazards |
| | `music_resolve` / `music_check` | time address to frame and bar.beat; frames off the beat grid |
| | `music_play` | play a range for the author to hear |
| | `beatgrid_propose` / `beatgrid_apply` | beat segments from the analysis; written as one undo step |
| `ContextTools` | `context_list` / `context_read` / `context_write` | the level's `ai/` folder: memory across sessions (a file, not an undo step) |
| | `guide_get` | a topic of the guide rendered from the public docs |

Prompts (`Prompts/WorkflowPrompts.cs`): `build_level_from_music`, `review_section`, `continue_level` -
short workflows over the tools above.

A list comes back as a header line (`total=.. offset=.. revision=.. scope=.. next=..`) and its lines;
anything else as compact JSON. A refusal is an error result carrying the code. Hints the game attaches
are dropped by `HintFilter` when sent within `AgentRules.HintRepeatAfterCalls` calls.

## Adding a tool

One NON-static class per area under `Tools/`, `[McpServerToolType]`, each method a thin forwarder
through `BridgeTool.ForwardAsync` with a `[Description]` written for the AI (what, units, cost). Then
list the class in `Program.cs` with `WithTools<T>(toolJson)`. A parameter type a tool takes must be
listed in `ToolJsonContext`: a trimmed binary has no reflection-based JSON and fails to start without it.
**Never `WithToolsFromAssembly()`**: a trimmed binary finds no tools through it.
Logging goes to stderr and the shared log file (see Logs), never stdout - stdout is the MCP transport.
