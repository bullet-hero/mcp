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
            StringAssert.Contains("open the game", refusal.Message);
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
            StringAssert.EndsWith("generators, design.", ServerInstructions.Text);
            Assert.IsFalse(ServerInstructions.Text.Contains('\r'));
        }
    }
}
