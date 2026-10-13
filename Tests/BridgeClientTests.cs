using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.MCP.Tools;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BH.MCP.Tests
{
    [TestFixture]
    public class BridgeClientTests
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

        private static async Task<BridgeException> Refusal(Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (BridgeException e)
            {
                return e;
            }

            Assert.Fail("The call was expected to be refused.");
            return null;
        }

        private static Task<JToken> Status(BridgeClient client)
            => client.CallAsync(AgentProtocol.Methods.SessionStatus, null, CancellationToken.None);

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task Handshake_Succeeds_AndTheConnectionIsReused()
        {
            using var bridge = new FakeBridge(_directory);
            using var client = new BridgeClient(bridge.EndpointPath);

            var first = await Status(client);
            var second = await Status(client);

            Assert.AreEqual("menu", (string)first["screen"]);
            Assert.AreEqual(true, (bool)first["connected"]);
            Assert.AreEqual("menu", (string)second["screen"]);
            Assert.AreEqual(1, bridge.ConnectionCount);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task AWrongToken_IsRefusedAsBadToken()
        {
            using var bridge = new FakeBridge(_directory, AgentEndpoint.NewToken());
            using var client = new BridgeClient(bridge.EndpointPath);

            var refusal = await Refusal(() => Status(client));

            Assert.AreEqual(AgentErrorCode.BadToken, refusal.Code);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ADifferentProtocol_IsRefusedAsProtocolMismatch_WithBothNumbers()
        {
            using var bridge = new FakeBridge(_directory) { ReplyProtocolMismatch = true };
            using var client = new BridgeClient(bridge.EndpointPath);

            var refusal = await Refusal(() => Status(client));

            Assert.AreEqual(AgentErrorCode.ProtocolMismatch, refusal.Code);
            Assert.AreEqual(AgentProtocol.Version + 1, refusal.ErrorData.ServerProtocol);
            Assert.AreEqual(AgentProtocol.Version, refusal.ErrorData.ClientProtocol);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Easy)]
        public async Task WithNoEndpointFile_TheAnswerIsNotConnected_TellingTheAiToAskTheAuthor()
        {
            using var client = new BridgeClient(Path.Combine(_directory, "endpoint.json"));

            var refusal = await Refusal(() => Status(client));

            Assert.AreEqual(AgentErrorCode.NotConnected, refusal.Code);
            StringAssert.Contains("open it", refusal.Message);
            StringAssert.Contains("enable MCP", refusal.Message);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task WithAStaleEndpointFile_NothingListens_TheAnswerIsNotConnected()
        {
            string path;

            using (var bridge = new FakeBridge(_directory)) path = bridge.EndpointPath;

            using var client = new BridgeClient(path);

            var refusal = await Refusal(() => Status(client));

            Assert.AreEqual(AgentErrorCode.NotConnected, refusal.Code);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ALostConnection_IsReopenedOnce_AndTheCallSucceeds()
        {
            using var bridge = new FakeBridge(_directory) { DropFirstConnectionAfterHello = true };
            using var client = new BridgeClient(bridge.EndpointPath);

            var result = await Status(client);

            Assert.AreEqual("menu", (string)result["screen"]);
            Assert.AreEqual(2, bridge.ConnectionCount);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task AnApplicationError_ReachesTheCaller_WithoutDroppingTheConnection()
        {
            using var bridge = new FakeBridge(_directory);
            using var client = new BridgeClient(bridge.EndpointPath);

            var refusal = await Refusal(() => client.CallAsync("no.such", null, CancellationToken.None));
            var after = await Status(client);

            Assert.AreEqual(AgentErrorCode.NotFound, refusal.Code);
            Assert.AreEqual("menu", (string)after["screen"]);
            Assert.AreEqual(1, bridge.ConnectionCount);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task TheTool_ReportsARefusalAsAnErrorResult_NamingTheCode()
        {
            using var client = new BridgeClient(Path.Combine(_directory, "endpoint.json"));
            var tools = new SessionTools(client);

            var result = await tools.SessionStatus(CancellationToken.None);

            Assert.IsTrue(result.IsError);
            var body = JObject.Parse(((TextContentBlock)result.Content.Single()).Text);
            Assert.AreEqual(AgentErrorCode.NotConnected, (string)body["error"]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task TheTool_ReturnsTheStatusAsJson()
        {
            using var bridge = new FakeBridge(_directory);
            using var client = new BridgeClient(bridge.EndpointPath);
            var tools = new SessionTools(client);

            var result = await tools.SessionStatus(CancellationToken.None);

            Assert.IsFalse(result.IsError == true);
            var body = JObject.Parse(((TextContentBlock)result.Content.Single()).Text);
            Assert.AreEqual("menu", (string)body["screen"]);
        }

        // The second game's endpoint, filed as the instance of pid beside the default endpoint file.
        private static void FileAsInstance(FakeBridge game, string defaultEndpointPath, int pid)
        {
            Assert.IsTrue(AgentEndpoint.TryRead(game.EndpointPath, out var endpoint, out var error), error);
            endpoint.Pid = pid;
            endpoint.Write(AgentEndpoint.InstancePath(defaultEndpointPath, pid));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task Select_SendsEveryCallToThatGame_AndNullGoesBackToTheDefault()
        {
            using var first = new FakeBridge(_directory);
            using var second = new FakeBridge(Directory.CreateDirectory(Path.Combine(_directory, "b")).FullName);
            FileAsInstance(second, first.EndpointPath, 222);
            using var client = new BridgeClient(first.EndpointPath);

            await client.SelectAsync(222);
            await Status(client);

            Assert.AreEqual(0, first.ConnectionCount);
            Assert.AreEqual(1, second.ConnectionCount);

            await client.SelectAsync(null);
            await Status(client);

            Assert.AreEqual(1, first.ConnectionCount);
            Assert.IsNull(client.SelectedPid);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task ASelectedGameThatIsGone_IsNotConnected_PointingAtInstanceList()
        {
            using var bridge = new FakeBridge(_directory);
            using var client = new BridgeClient(bridge.EndpointPath);

            await client.SelectAsync(333);
            var refusal = await Refusal(() => Status(client));

            Assert.AreEqual(AgentErrorCode.NotConnected, refusal.Code);
            StringAssert.Contains("pid 333", refusal.Message);
            StringAssert.Contains("instance_list", refusal.Message);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Normal)]
        public async Task InstanceTools_ListOnlyLiveGames_AndSelectRefusesAPidNotRunning()
        {
            using var first = new FakeBridge(_directory);
            using var second = new FakeBridge(Directory.CreateDirectory(Path.Combine(_directory, "b")).FullName);
            var live = Environment.ProcessId;
            const int dead = int.MaxValue - 7;
            FileAsInstance(second, first.EndpointPath, live);
            FileAsInstance(second, first.EndpointPath, dead);
            using var client = new BridgeClient(first.EndpointPath);
            var tools = new SessionTools(client);

            var listed = JObject.Parse(((TextContentBlock)tools.InstanceList().Content.Single()).Text);
            var refused = await tools.InstanceSelect(dead);
            var selected = await tools.InstanceSelect(live);
            await Status(client);

            var instances = (JArray)listed["instances"];
            Assert.AreEqual(1, instances.Count);
            Assert.AreEqual(live, (int)instances[0]["pid"]);
            Assert.IsFalse((bool)instances[0]["isDefault"]);
            Assert.IsTrue(refused.IsError);
            Assert.IsFalse(selected.IsError == true);
            Assert.AreEqual(1, second.ConnectionCount);
        }
    }

    [TestFixture]
    public class ServerInstructionsTests
    {
        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void TheText_FitsWhatAClientKeeps()
        {
            Assert.Less(ServerInstructions.Text.Length, ServerInstructions.MaxLength);
            StringAssert.StartsWith("You work in the Bullet Hero game", ServerInstructions.Text);
            StringAssert.EndsWith("generators, design, pictures, edit.", ServerInstructions.Text);
            Assert.IsFalse(ServerInstructions.Text.Contains('\r'));
        }
    }
}
