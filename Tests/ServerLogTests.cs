using System;
using System.IO;
using System.Linq;
using System.Text;
using BH.MCP.Tools;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace BH.MCP.Tests
{
    [TestFixture]
    public class ServerLogTests
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

        private string LogPath => Path.Combine(_directory, "server.log");

        private void Append(string text) => File.AppendAllText(LogPath, text, new UTF8Encoding(false));

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void PathFor_SitsInLogsBesideTheEndpoint()
        {
            var endpoint = Path.Combine(_directory, "mcp", "endpoint.json");

            Assert.AreEqual(Path.Combine(_directory, "mcp", "logs", "server.log"), ServerLog.PathFor(endpoint));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void Line_IsOneLineWithPidLevelAndShortCategory()
        {
            var line = ServerLog.Line(new DateTime(2026, 10, 12, 14, 3, 5, 7), 42, LogLevel.Warning, "BH.MCP.Calls",
                "first\r\nsecond");

            Assert.AreEqual("2026-10-12 14:03:05.007 [42] warn Calls: first | second", line);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Rotate_MovesAnOversizedFileAside_AndKeepsASmallOne()
        {
            Append("small\n");
            ServerLog.Rotate(LogPath, 100);
            Assert.IsTrue(File.Exists(LogPath));

            Append(new string('x', 200) + "\n");
            ServerLog.Rotate(LogPath, 100);

            Assert.IsFalse(File.Exists(LogPath));
            Assert.IsTrue(File.Exists(Path.Combine(_directory, ServerLog.PreviousFileName)));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void FileLogger_WritesLinesAtOrAboveItsLevel()
        {
            using (var provider = new FileLoggerProvider(LogPath, LogLevel.Information))
            {
                var logger = provider.CreateLogger("BH.MCP.Calls");
                logger.LogDebug("hidden");
                logger.LogInformation("edit_batch 12 ms ok");
            }

            var lines = File.ReadAllLines(LogPath);
            Assert.AreEqual(1, lines.Length);
            StringAssert.EndsWith("info Calls: edit_batch 12 ms ok", lines[0]);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Tail_ReturnsOnlyCompleteNewLines()
        {
            var tail = new LogTail(LogPath);
            CollectionAssert.IsEmpty(tail.ReadNew());

            Append("one\ntw");
            CollectionAssert.AreEqual(new[] { "one" }, tail.ReadNew());

            Append("o\r\nthree\n");
            CollectionAssert.AreEqual(new[] { "two", "three" }, tail.ReadNew());
            CollectionAssert.IsEmpty(tail.ReadNew());
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Tail_KeepsACharacterSplitAcrossTwoReads()
        {
            var bytes = Encoding.UTF8.GetBytes("ИИ\n");
            var tail = new LogTail(LogPath);

            File.WriteAllBytes(LogPath, bytes.Take(3).ToArray());
            CollectionAssert.IsEmpty(tail.ReadNew());

            using (var stream = new FileStream(LogPath, FileMode.Append)) stream.Write(bytes, 3, bytes.Length - 3);
            CollectionAssert.AreEqual(new[] { "ИИ" }, tail.ReadNew());
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Tail_StartsOverWhenTheFileShrinks()
        {
            var tail = new LogTail(LogPath);
            Append("a long first line\n");
            tail.ReadNew();

            File.WriteAllText(LogPath, "new\n");
            CollectionAssert.AreEqual(new[] { "new" }, tail.ReadNew());
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void TailFromEnd_SkipsThePartLineItLandsIn()
        {
            Append("first line\nsecond\nthird\n");

            var tail = LogTail.FromEnd(LogPath, 10);

            CollectionAssert.AreEqual(new[] { "third" }, tail.ReadNew());
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void CallLine_NamesTheToolTimeAndOutcome()
        {
            Assert.AreEqual("edit_batch 12 ms ok", BridgeTool.CallLine("edit.batch", 12.4, null, null));
            Assert.AreEqual("edit_batch 3 ms stale: The level is at revision 3, not 1.",
                BridgeTool.CallLine("edit.batch", 3, "stale", "The level is at revision 3, not 1."));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void ClientConfig_IsTheShapeTheGameCopies()
        {
            Assert.AreEqual(
                "{\"mcpServers\":{\"bullet-hero\":{\"command\":\"C:\\\\g\\\\BH.MCP.exe\",\"args\":[\"--endpoint\",\"e.json\"]}}}",
                LogViewer.ClientConfig(@"C:\g\BH.MCP.exe", "e.json"));
        }
    }
}
