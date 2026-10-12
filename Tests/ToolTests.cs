using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using BH.MCP.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BH.MCP.Tests
{
    [TestFixture]
    public class ToolTests
    {
        private string _directory;

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "bh-mcp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static readonly Type[] ToolClasses =
        {
            typeof(SessionTools), typeof(LevelTools), typeof(ReadTools), typeof(EditTools), typeof(HistoryTools),
            typeof(SelectionTools), typeof(PrefabTools), typeof(GeneratorTools), typeof(MaintenanceTools),
            typeof(VisionTools), typeof(CaptureTools), typeof(MusicTools),
            typeof(ContextTools), typeof(AppTools),
        };

        // Tools the server answers itself, with no bridge method behind them.
        private static readonly string[] ServerOnlyTools = { "app_launch", "instance_list", "instance_select" };

        private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

        private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content.Single()).Text;

        // One row per tool: the method it must reach and the params it must send (null = none).
        public static IEnumerable<TestCaseData> Calls()
        {
            yield return Row("session_status", AgentProtocol.Methods.SessionStatus, null,
                t => t.Session.SessionStatus(default));
            yield return Row("app_navigate", AgentProtocol.Methods.AppNavigate, "{\"args\":\"--editor\"}",
                t => t.App.AppNavigate("--editor", default));
            yield return Row("level_list", AgentProtocol.Methods.LevelList, "{\"filter\":\"zero\",\"offset\":5,\"limit\":9}",
                t => t.Level.LevelList("zero", 5, 9));
            yield return Row("level_open", AgentProtocol.Methods.LevelOpen, "{\"id\":\"abc\"}",
                t => t.Level.LevelOpen("abc"));
            yield return Row("level_create", AgentProtocol.Methods.LevelCreate,
                "{\"name\":\"N\",\"description\":\"D\",\"framerate\":30,\"duration\":\"s:90\"}",
                t => t.Level.LevelCreate("N", "D", 30, Json("\"s:90\"")));
            yield return Row("level_save", AgentProtocol.Methods.LevelSave, null, t => t.Level.LevelSave());
            yield return Row("level_outline", AgentProtocol.Methods.LevelOutline,
                "{\"parent\":3,\"depth\":2,\"type\":\"shape\",\"name\":\"ring\",\"from\":120,\"to\":\"s:12.5\","
                + "\"offset\":1,\"limit\":2}",
                t => t.Read.LevelOutline(3, 2, "shape", "ring", Json("120"), Json("\"s:12.5\""), 1, 2));
            yield return Row("object_query", AgentProtocol.Methods.ObjectQuery,
                "{\"from\":\"f:10\",\"name\":\"a\",\"type\":\"text\",\"parent\":7,\"limit\":4}",
                t => t.Read.ObjectQuery(Json("\"f:10\""), null, "a", "text", 7, null, 4));
            yield return Row("object_get", AgentProtocol.Methods.ObjectGet, "{\"ids\":[1,2]}",
                t => t.Read.ObjectGet(new[] { 1, 2 }));
            yield return Row("keys_get", AgentProtocol.Methods.KeysGet,
                "{\"id\":4,\"tracks\":[\"position\",\"size\"],\"from\":5,\"to\":\"s:2\",\"offset\":1,\"limit\":3}",
                t => t.Read.KeysGet(4, new[] { "position", "size" }, Json("5"), Json("\"s:2\""), 1, 3));
            yield return Row("levelkeys_get", AgentProtocol.Methods.LevelKeysGet,
                "{\"owner\":\"camera\",\"tracks\":[\"zoom\"],\"to\":\"s:2\",\"limit\":3}",
                t => t.Read.LevelKeysGet("camera", new[] { "zoom" }, null, Json("\"s:2\""), null, 3));
            yield return Row("events_get", AgentProtocol.Methods.EventsGet, "{\"kinds\":[\"beats\"],\"limit\":7}",
                t => t.Read.EventsGet(new[] { "beats" }, null, 7));
            yield return Row("resources_list", AgentProtocol.Methods.ResourcesList, "{\"kind\":\"fonts\",\"offset\":2}",
                t => t.Read.ResourcesList("fonts", 2));
            yield return Row("shapes_find", AgentProtocol.Methods.ShapesFind,
                "{\"form\":\"Ring\",\"sector\":\"Half\",\"thickness\":\"Thin\",\"name\":\"r\",\"limit\":5}",
                t => t.Read.ShapesFind("Ring", "Half", "Thin", "r", null, 5));
            yield return Row("edit_batch", AgentProtocol.Methods.EditBatch,
                "{\"expectedRevision\":41,\"caption\":\"C\",\"ops\":[{\"op\":\"name\",\"id\":\"$a\",\"value\":\"x\"}]}",
                t => t.Edit.EditBatch(41, "C", Json("[{\"op\":\"name\",\"id\":\"$a\",\"value\":\"x\"}]")));
            yield return Row("history_get", AgentProtocol.Methods.HistoryGet, "{\"offset\":3}",
                t => t.History.HistoryGet(3));
            yield return Row("history_undo", AgentProtocol.Methods.HistoryUndo, "{\"expectedRevision\":9}",
                t => t.History.HistoryUndo(9));
            yield return Row("history_redo", AgentProtocol.Methods.HistoryRedo, "{}", t => t.History.HistoryRedo());
            yield return Row("changes_since", AgentProtocol.Methods.ChangesSince, "{\"revision\":12}",
                t => t.History.ChangesSince(12));
            yield return Row("selection_get", AgentProtocol.Methods.SelectionGet, null, t => t.Selection.SelectionGet());
            yield return Row("selection_set", AgentProtocol.Methods.SelectionSet, "{\"ids\":[5,6]}",
                t => t.Selection.SelectionSet(new[] { 5, 6 }));
            yield return Row("prefab_mode_enter", AgentProtocol.Methods.PrefabModeEnter, "{\"id\":\"g\"}",
                t => t.Prefab.PrefabModeEnter("g"));
            yield return Row("prefab_mode_exit", AgentProtocol.Methods.PrefabModeExit, "{\"all\":true}",
                t => t.Prefab.PrefabModeExit(true));
            yield return Row("prefab_overrides", AgentProtocol.Methods.PrefabOverrides, "{\"id\":8,\"limit\":3}",
                t => t.Prefab.PrefabOverrides(8, null, 3));
            yield return Row("generator_list", AgentProtocol.Methods.GeneratorList, "{\"offset\":1}",
                t => t.Generator.GeneratorList(1));
            yield return Row("generator_schema", AgentProtocol.Methods.GeneratorSchema, "{\"id\":\"gen_x\"}",
                t => t.Generator.GeneratorSchema("gen_x"));
            yield return Row("generator_run", AgentProtocol.Methods.GeneratorRun,
                "{\"expectedRevision\":3,\"id\":\"gen_x\",\"parameters\":{\"Count\":4},\"start\":1,\"end\":\"s:2\","
                + "\"parent\":5,\"layer\":2,\"seed\":4000000000,\"selection\":[1,2],\"groupName\":\"G\","
                + "\"splitLayers\":true,\"confirmDangerous\":false,\"caption\":\"C\"}",
                t => t.Generator.GeneratorRun(3, "gen_x", Json("1"), Json("\"s:2\""), Json("{\"Count\":4}"),
                    5, 2, 4000000000, new[] { 1, 2 }, "G", true, false, "C"));
            yield return Row("resource_import", AgentProtocol.Methods.ResourceImport,
                "{\"expectedRevision\":2,\"path\":\"C:/a.png\",\"kind\":\"texture\"}",
                t => t.Maintenance.ResourceImport(2, "C:/a.png", "texture"));
            yield return Row("raw_get", AgentProtocol.Methods.RawGet, "{\"path\":\"level.game\",\"limit\":4}",
                t => t.Maintenance.RawGet("level.game", null, 4));
            yield return Row("raw_set", AgentProtocol.Methods.RawSet,
                "{\"expectedRevision\":6,\"path\":\"level.stg.seed\",\"value\":42}",
                t => t.Maintenance.RawSet(6, "level.stg.seed", Json("42")));
            yield return Row("command_list", AgentProtocol.Methods.CommandList, "{}",
                t => t.Maintenance.CommandList());
            yield return Row("command_run", AgentProtocol.Methods.CommandRun,
                "{\"expectedRevision\":7,\"id\":\"Duplicate\"}", t => t.Maintenance.CommandRun(7, "Duplicate"));
            yield return Row("validate_list", AgentProtocol.Methods.Validate, "{\"limit\":10}",
                t => t.Maintenance.ValidateList(null, 10));
            yield return Row("validate_fix", AgentProtocol.Methods.ValidateFix,
                "{\"expectedRevision\":8,\"rules\":[\"a\",\"b\"]}",
                t => t.Maintenance.ValidateFix(8, new[] { "a", "b" }));
            yield return Row("view_schematic", AgentProtocol.Methods.ViewSchematic,
                "{\"frame\":\"s:2\",\"region\":{\"x\":-1.0,\"y\":-2.0,\"width\":10.0,\"height\":5.0},"
                + "\"size\":512,\"labels\":false}",
                t => t.Vision.ViewSchematic(Json("\"s:2\""), Json("{\"x\":-1,\"y\":-2,\"width\":10,\"height\":5}"),
                    512, false));
            yield return Row("frame_stats", AgentProtocol.Methods.FrameStats,
                "{\"from\":1,\"to\":\"s:10\",\"step\":\"f:30\"}",
                t => t.Vision.FrameStats(Json("1"), Json("\"s:10\""), Json("\"f:30\"")));
            yield return Row("cursor_resolve", AgentProtocol.Methods.CursorResolve, "{\"ids\":[3,4],\"frame\":120}",
                t => t.Vision.CursorResolve(new[] { 3, 4 }, Json("120")));
            yield return Row("object_explain", AgentProtocol.Methods.ObjectExplain, "{\"id\":9,\"frame\":\"f:5\"}",
                t => t.Vision.ObjectExplain(9, Json("\"f:5\"")));
            yield return Row("shapes_atlas", AgentProtocol.Methods.ShapesAtlas,
                "{\"form\":\"Ring\",\"name\":\"r\",\"offset\":99}",
                t => t.Vision.ShapesAtlas("Ring", null, null, "r", 99));
            yield return Row("view_capture", AgentProtocol.Methods.ViewCapture,
                "{\"frame\":\"s:3\",\"size\":400,\"region\":{\"x\":-1.5,\"y\":-1.5,\"width\":2.5,\"height\":1.5}}",
                t => t.Capture.ViewCapture(Json("\"s:3\""), 400,
                    Json("{\"x\":-1.5,\"y\":-1.5,\"width\":2.5,\"height\":1.5}")));
            yield return Row("view_filmstrip", AgentProtocol.Methods.ViewFilmstrip,
                "{\"frames\":[120,\"s:5\"],\"size\":600}",
                t => t.Capture.ViewFilmstrip(frames: Json("[120,\"s:5\"]"), size: 600));
            yield return Row("playhead_get", AgentProtocol.Methods.PlayheadGet, null, t => t.Capture.PlayheadGet());
            yield return Row("playhead_set", AgentProtocol.Methods.PlayheadSet, "{\"frame\":\"f:90\"}",
                t => t.Capture.PlayheadSet(Json("\"f:90\"")));
            yield return Row("playback_set", AgentProtocol.Methods.PlaybackSet, "{\"playing\":false,\"frame\":30}",
                t => t.Capture.PlaybackSet(false, Json("30")));
            yield return Row("music_analyze", AgentProtocol.Methods.MusicAnalyze, "{\"resource\":-2}",
                t => t.Music.MusicAnalyze(-2));
            yield return Row("music_table", AgentProtocol.Methods.MusicTable,
                "{\"from\":\"bar:2\",\"by\":\"beat\",\"limit\":5}",
                t => t.Music.MusicTable(Json("\"bar:2\""), null, "beat", null, null, 5));
            yield return Row("music_strip", AgentProtocol.Methods.MusicStrip, "{\"to\":\"s:20\",\"size\":500}",
                t => t.Music.MusicStrip(null, Json("\"s:20\""), 500));
            yield return Row("music_resolve", AgentProtocol.Methods.MusicResolve, "{\"time\":\"bar:17.3\"}",
                t => t.Music.MusicResolve(Json("\"bar:17.3\"")));
            yield return Row("music_check", AgentProtocol.Methods.MusicCheck, "{\"tolerance\":2}",
                t => t.Music.MusicCheck(2));
            yield return Row("music_play", AgentProtocol.Methods.MusicPlay, "{\"from\":\"bar:17\",\"to\":\"bar:19\"}",
                t => t.Music.MusicPlay(Json("\"bar:17\""), Json("\"bar:19\"")));
            yield return Row("beatgrid_propose", AgentProtocol.Methods.BeatgridPropose, "{\"from\":30}",
                t => t.Music.BeatgridPropose(null, Json("30")));
            yield return Row("beatgrid_apply", AgentProtocol.Methods.BeatgridApply,
                "{\"expectedRevision\":4,\"segments\":[{\"start\":1,\"duration\":\"s:10\",\"bpm\":120.0,"
                + "\"offset\":0.0,\"beatsPerBar\":4}],\"caption\":\"G\"}",
                t => t.Music.BeatgridApply(4, Json("[{\"start\":1,\"duration\":\"s:10\",\"bpm\":120,"
                    + "\"offset\":0,\"beatsPerBar\":4}]"), "G"));
            yield return Row("context_list", AgentProtocol.Methods.ContextList, null, t => t.Context.ContextList());
            yield return Row("context_read", AgentProtocol.Methods.ContextRead, "{\"file\":\"plan.md\"}",
                t => t.Context.ContextRead("plan.md"));
            yield return Row("context_write", AgentProtocol.Methods.ContextWrite,
                "{\"appendSection\":{\"heading\":\"Decisions\",\"text\":\"t\"}}",
                t => t.Context.ContextWrite(heading: "Decisions", text: "t"));
            yield return Row("guide_get", AgentProtocol.Methods.GuideGet, "{\"topic\":\"time\"}",
                t => t.Context.GuideGet("time"));
        }

        // Variants of a row above: another shape of the same call, beyond the one row per method.
        public static IEnumerable<TestCaseData> Variants()
        {
            yield return Row("edit_batch (ops as a JSON string)", AgentProtocol.Methods.EditBatch,
                "{\"expectedRevision\":41,\"caption\":\"C\",\"ops\":[{\"op\":\"name\",\"id\":4,\"value\":\"x\"}]}",
                t => t.Edit.EditBatch(41, "C", Json("\"[{\\\"op\\\":\\\"name\\\",\\\"id\\\":4,\\\"value\\\":\\\"x\\\"}]\"")));
            yield return Row("command_list (all)", AgentProtocol.Methods.CommandList, "{\"all\":true}",
                t => t.Maintenance.CommandList(true));
        }

        private static TestCaseData Row(string tool, string method, string expectedParams,
            Func<Tools, Task<CallToolResult>> call)
            => new TestCaseData(method, expectedParams, call).SetName(tool);

        public sealed class Tools
        {
            public readonly SessionTools Session;
            public readonly LevelTools Level;
            public readonly ReadTools Read;
            public readonly EditTools Edit;
            public readonly HistoryTools History;
            public readonly SelectionTools Selection;
            public readonly PrefabTools Prefab;
            public readonly GeneratorTools Generator;
            public readonly MaintenanceTools Maintenance;
            public readonly VisionTools Vision;
            public readonly CaptureTools Capture;
            public readonly MusicTools Music;
            public readonly ContextTools Context;
            public readonly AppTools App;

            public Tools(BridgeClient client)
            {
                App = new AppTools(client, new GameLauncher(client, null));
                Prefab = new PrefabTools(client);
                Generator = new GeneratorTools(client);
                Maintenance = new MaintenanceTools(client);
                Vision = new VisionTools(client);
                Capture = new CaptureTools(client);
                Music = new MusicTools(client);
                Context = new ContextTools(client);
                Session = new SessionTools(client);
                Level = new LevelTools(client);
                Read = new ReadTools(client);
                Edit = new EditTools(client);
                History = new HistoryTools(client);
                Selection = new SelectionTools(client);
            }
        }

        private static FakeBridge Echo(string directory)
        {
            var bridge = new FakeBridge(directory);
            bridge.Responder = request => FakeBridge.Ok(request, new JObject { ["revision"] = 1 });
            return bridge;
        }

        [TestCaseSource(nameof(Calls))]
        [TestCaseSource(nameof(Variants))]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task EveryTool_ForwardsToItsMethod_WithItsParams(string method, string expectedParams,
            Func<Tools, Task<CallToolResult>> call)
        {
            using var bridge = Echo(_directory);
            using var client = new BridgeClient(bridge.EndpointPath);

            var result = await call(new Tools(client));

            Assert.IsFalse(result.IsError == true, TextOf(result));
            Assert.IsTrue(bridge.Requests.TryDequeue(out var request));
            Assert.AreEqual(method, request.Method);

            if (expectedParams == null)
                Assert.IsTrue(request.Params == null || request.Params.Type == JTokenType.Null);
            else
                Assert.IsTrue(JToken.DeepEquals(JToken.Parse(expectedParams), request.Params),
                    "sent " + request.Params);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void EveryBridgeMethod_HasATool_AndNothingElseIsExposed()
        {
            var methods = typeof(AgentProtocol.Methods).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (string)f.GetRawConstantValue())
                .Where(m => m != AgentProtocol.Methods.Hello)
                .Select(AgentProtocol.ToolName)
                .ToArray();

            var tools = ToolMethods().Select(m => m.GetCustomAttribute<McpServerToolAttribute>().Name)
                .OrderBy(n => n).ToArray();

            Assert.AreEqual(methods.Concat(ServerOnlyTools).OrderBy(n => n).ToArray(), tools);
            Assert.AreEqual(methods.Length, Calls().Count());
        }

        private static IEnumerable<MethodInfo> ToolMethods()
            => ToolClasses.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void EveryToolClass_IsNonStatic_AndEveryToolAndParameterIsDescribed()
        {
            foreach (var type in ToolClasses)
            {
                Assert.IsFalse(type.IsAbstract && type.IsSealed, type.Name + " is static: trimming drops it");
                Assert.IsNotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>(), type.Name);
            }

            foreach (var method in ToolMethods())
            {
                Assert.IsNotEmpty(method.GetCustomAttribute<DescriptionAttribute>()?.Description, method.Name);

                foreach (var parameter in method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)))
                    Assert.IsNotEmpty(parameter.GetCustomAttribute<DescriptionAttribute>()?.Description,
                        method.Name + "." + parameter.Name);
            }
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void EveryPrompt_IsDescribed_AndNamesOnlyToolsThatExist()
        {
            var type = typeof(BH.MCP.Prompts.WorkflowPrompts);
            Assert.IsFalse(type.IsAbstract && type.IsSealed, "a static prompt class is dropped by trimming");

            var tools = new HashSet<string>(ToolMethods().Select(m => m.GetCustomAttribute<McpServerToolAttribute>().Name));
            var prompts = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null).ToArray();
            Assert.AreEqual(4, prompts.Length);

            foreach (var prompt in prompts)
            {
                Assert.IsNotEmpty(prompt.GetCustomAttribute<DescriptionAttribute>()?.Description, prompt.Name);

                var args = prompt.GetParameters().Select(p => (object)"x").ToArray();
                var text = (string)prompt.Invoke(new BH.MCP.Prompts.WorkflowPrompts(), args);
                Assert.IsFalse(text.Contains('—') || text.Contains('–'), prompt.Name);

                foreach (System.Text.RegularExpressions.Match word in
                         System.Text.RegularExpressions.Regex.Matches(text, @"\b[a-z]+_[a-z_]+\b"))
                    Assert.IsTrue(tools.Contains(word.Value), prompt.Name + " names " + word.Value);
            }
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void NoDescriptionHoldsAnEmOrEnDash()
        {
            foreach (var method in ToolMethods())
            {
                var texts = new[] { method.GetCustomAttribute<DescriptionAttribute>().Description }
                    .Concat(method.GetParameters().Select(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""));

                foreach (var text in texts)
                {
                    Assert.IsFalse(text.Contains('—') || text.Contains('–'), method.Name);
                }
            }
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ARefusedBatch_IsAnErrorResult_WithTheCodeAndTheCurrentRevision()
        {
            using var bridge = new FakeBridge(_directory);
            bridge.Responder = request => FakeBridge.Fail(request, AgentErrorCode.Stale,
                new AgentErrorData { Revision = 44 });
            using var client = new BridgeClient(bridge.EndpointPath);

            var result = await new EditTools(client).EditBatch(41, "C", Json("[]"));

            Assert.IsTrue(result.IsError);
            var body = JObject.Parse(TextOf(result));
            Assert.AreEqual(AgentErrorCode.Stale, (string)body["error"]);
            Assert.AreEqual(44, (int)body["data"]["revision"]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ABatchFile_IsReadByTheServer_AsAnArrayOrUnderOps_AndOpsWithItIsRefused()
        {
            var plain = Path.Combine(_directory, "plain.json");
            var wrapped = Path.Combine(_directory, "wrapped.json");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(plain, "[{\"op\":\"name\",\"id\":4,\"value\":\"x\"},{\"op\":\"delete\",\"ids\":[5]}]");
            File.WriteAllText(wrapped, "{\"ops\":[{\"op\":\"name\",\"id\":4,\"value\":\"y\"}]}");

            JToken sent = null;
            using var bridge = new FakeBridge(_directory);
            bridge.Responder = request =>
            {
                sent = request.Params;
                return FakeBridge.Ok(request, new JObject { ["revision"] = 42 });
            };
            using var client = new BridgeClient(bridge.EndpointPath);
            var tools = new EditTools(client);

            Assert.IsFalse((await tools.EditBatch(41, "C", opsFile: plain)).IsError == true);
            Assert.AreEqual(2, ((JArray)sent["ops"]).Count);

            Assert.IsFalse((await tools.EditBatch(41, "C", opsFile: wrapped)).IsError == true);
            Assert.AreEqual("y", (string)sent["ops"][0]["value"]);

            Assert.IsTrue((await tools.EditBatch(41, "C", Json("[]"), wrapped)).IsError);
            Assert.IsTrue((await tools.EditBatch(41, "C")).IsError);
            Assert.IsTrue((await tools.EditBatch(41, "C", opsFile: plain + ".missing")).IsError);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task APictureAnswer_IsAnImageBlock_ThenTheRestAsText()
        {
            var png = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
            using var bridge = new FakeBridge(_directory);
            bridge.Responder = request => FakeBridge.Ok(request, new JObject
            {
                ["revision"] = 3, ["png"] = Convert.ToBase64String(png), ["width"] = 4, ["legend"] = "L",
            });
            using var client = new BridgeClient(bridge.EndpointPath);

            var result = await new VisionTools(client).ViewSchematic(Json("1"));

            Assert.IsFalse(result.IsError == true);
            Assert.AreEqual(2, result.Content.Count);
            var image = (ImageContentBlock)result.Content[0];
            Assert.AreEqual("image/png", image.MimeType);
            CollectionAssert.AreEqual(png, Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(image.Data.ToArray())));
            var text = JObject.Parse(((TextContentBlock)result.Content[1]).Text);
            Assert.IsNull(text["png"]);
            Assert.AreEqual("L", (string)text["legend"]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task WithNoGame_EveryToolAnswersNotConnected()
        {
            using var client = new BridgeClient(Path.Combine(_directory, "endpoint.json"));

            var result = await new ReadTools(client).LevelOutline();

            Assert.IsTrue(result.IsError);
            Assert.AreEqual(AgentErrorCode.NotConnected, (string)JObject.Parse(TextOf(result))["error"]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task APagedAnswer_IsAHeaderLineAndItsLines_WithNextWhenMoreRemain()
        {
            using var bridge = new FakeBridge(_directory);
            bridge.Responder = request => FakeBridge.Ok(request, JObject.FromObject(new AgentPagedResult
            {
                Total = 5, Offset = 2, Revision = 7, Scope = "level", Lines = { "#1 Shape \"a\"", "#2 Shape \"b\"" },
            }));
            using var client = new BridgeClient(bridge.EndpointPath);

            var text = TextOf(await new ReadTools(client).LevelOutline(offset: 2, limit: 2));

            Assert.AreEqual("total=5 offset=2 revision=7 scope=level next=4\n#1 Shape \"a\"\n#2 Shape \"b\"", text);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void ALastPage_HasNoNext()
        {
            var page = JObject.FromObject(new AgentPagedResult { Total = 1, Revision = 3, Scope = "level", Lines = { "x" } });

            Assert.AreEqual("total=1 offset=0 revision=3 scope=level\nx", BridgeTool.Shape(page, new HintFilter()));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void APage_KeepsItsOwnFields_ShortInTheHeader_LongOnTheirOwnLine()
        {
            var page = JObject.FromObject(new AgentPagedResult { Total = 1, Revision = 3, Scope = "level", Lines = { "x" } });
            page["errors"] = 2;
            page["grid"] = "level";
            page["legend"] = "rows follow the level's beat grid";

            Assert.AreEqual("total=1 offset=0 revision=3 scope=level errors=2 grid=level\n" +
                            "legend: rows follow the level's beat grid\nx", BridgeTool.Shape(page, new HintFilter()));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public void ASingleResult_IsCompactJson()
        {
            var result = JObject.FromObject(new SelectionResult { Revision = 2, Ids = { 3 } });

            var text = BridgeTool.Shape(result, new HintFilter());

            Assert.IsFalse(text.Contains('\n'));
            Assert.AreEqual(3, (int)JObject.Parse(text)["ids"][0]);
        }

        private static JObject WithHint(string key, string text)
            => JObject.FromObject(new SelectionResult
            {
                Hints = new List<AgentHint> { new AgentHint { Key = key, Text = text } },
            });

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public void AHint_IsSentOnce_ThenDropped_UntilTheRepeatWindowPasses()
        {
            var filter = new HintFilter();
            var sentAt = new List<int>();

            for (var call = 1; call <= AgentRules.HintRepeatAfterCalls + 2; call++)
            {
                filter.BeginCall();
                var text = BridgeTool.Shape(WithHint(AgentHintKey.EffectiveLayer, "layers add up"), filter);
                if (JObject.Parse(text)["hints"] != null) sentAt.Add(call);
            }

            Assert.AreEqual(new[] { 1, 1 + AgentRules.HintRepeatAfterCalls }, sentAt);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public void HintsAreSuppressedPerKey_AndAPageCarriesItsHintsLast()
        {
            var filter = new HintFilter();
            filter.BeginCall();

            var first = JObject.FromObject(new AgentPagedResult
            {
                Total = 1, Lines = { "x" },
                Hints = new List<AgentHint>
                {
                    new AgentHint { Key = AgentHintKey.ZeroSizeGroup, Text = "no size" },
                    new AgentHint { Key = AgentHintKey.LocalKeyframes, Text = "global frames" },
                },
            });
            var text = BridgeTool.Shape(first, filter);

            filter.BeginCall();
            var again = BridgeTool.Shape(WithHint(AgentHintKey.ZeroSizeGroup, "no size"), filter);
            var other = BridgeTool.Shape(WithHint(AgentHintKey.StaleRead, "re-read"), filter);

            StringAssert.EndsWith("\nhint: no size\nhint: global frames", text);
            Assert.IsNull(JObject.Parse(again)["hints"]);
            Assert.AreEqual("re-read", (string)JObject.Parse(other)["hints"][0]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ARepeatedHint_IsDroppedFromTheSecondToolResult()
        {
            using var bridge = new FakeBridge(_directory);
            bridge.Responder = request => FakeBridge.Ok(request, WithHint(AgentHintKey.PrefabMode, "in prefab mode"));
            using var client = new BridgeClient(bridge.EndpointPath);
            var tools = new SelectionTools(client);

            var first = JObject.Parse(TextOf(await tools.SelectionGet()));
            var second = JObject.Parse(TextOf(await tools.SelectionGet()));

            Assert.AreEqual("in prefab mode", (string)first["hints"][0]);
            Assert.IsNull(second["hints"]);
        }
    }
}
