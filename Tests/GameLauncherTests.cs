using System;
using System.IO;
using System.Runtime.InteropServices;
using BH.SDK.Agent;
using NUnit.Framework;

namespace BH.MCP.Tests
{
    /// <summary> GameLauncher: which file is the game, and whether it is already running. </summary>
    [TestFixture]
    public class GameLauncherTests
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

        private string File(string name)
        {
            var path = Path.Combine(_directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, string.Empty);
            return path;
        }

        private void Folder(string name) => Directory.CreateDirectory(Path.Combine(_directory, name));

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Windows_TheExeWithAData_Folder_IsTheGame()
        {
            var own = File("Bullet Hero MCP.exe");
            var game = File("Bullet Hero.exe");
            File("UnityCrashHandler64.exe");
            Folder("Bullet Hero_Data");

            Assert.AreEqual(game, GameLauncher.Locate(_directory, own, null, OSPlatform.Windows));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Windows_NoDataFolder_OrTwoCandidates_IsNoGame()
        {
            var own = File("Bullet Hero MCP.exe");
            File("Bullet Hero.exe");
            Assert.IsNull(GameLauncher.Locate(_directory, own, null, OSPlatform.Windows), "no _Data folder");

            Folder("Bullet Hero_Data");
            File("Other.exe");
            Folder("Other_Data");
            Assert.IsNull(GameLauncher.Locate(_directory, own, null, OSPlatform.Windows), "two candidates");
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Linux_TheX86_64Player_IsTheGame()
        {
            var own = File("Bullet Hero MCP");
            var game = File("Bullet Hero.x86_64");
            File("UnityPlayer.so");
            Folder("Bullet Hero_Data");

            Assert.AreEqual(game, GameLauncher.Locate(_directory, own, null, OSPlatform.Linux));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void Mac_TheEnclosingBundle_IsTheGame()
        {
            var server = Path.Combine(_directory, "Bullet Hero.app", "Contents", "Resources", "mcp", "osx-arm64");
            Directory.CreateDirectory(server);

            Assert.AreEqual(Path.Combine(_directory, "Bullet Hero.app"),
                GameLauncher.Locate(server, Path.Combine(server, "Bullet Hero MCP"), null, OSPlatform.OSX));
            Assert.IsNull(GameLauncher.Locate(_directory, null, null, OSPlatform.OSX), "not inside a bundle");
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void AnOverride_WinsWhenItExists()
        {
            var own = File("Bullet Hero MCP.exe");
            File("Bullet Hero.exe");
            Folder("Bullet Hero_Data");
            var elsewhere = File(Path.Combine("elsewhere", "Game.exe"));

            Assert.AreEqual(elsewhere, GameLauncher.Locate(_directory, own, elsewhere, OSPlatform.Windows));
            Assert.IsNull(GameLauncher.Locate(_directory, own, Path.Combine(_directory, "missing.exe"),
                OSPlatform.Windows));
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void RunningGamePid_FindsAProcessByItsExecutable_WithNoEndpoint()
        {
            var os = GameLauncher.CurrentPlatform();

            Assert.AreEqual(Environment.ProcessId, GameLauncher.RunningGamePid(Environment.ProcessPath, os));
            Assert.IsNull(GameLauncher.RunningGamePid(File("Bullet Hero.exe"), os), "nothing runs this file");
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.Hard)]
        public void RunningPid_IsTheLiveProcessTheEndpointNames()
        {
            var path = Path.Combine(_directory, "endpoint.json");
            Assert.IsNull(GameLauncher.RunningPid(path), "no file");

            AgentEndpoint.Create(1234, Environment.ProcessId, "1.0.0").Write(path);
            Assert.AreEqual(Environment.ProcessId, GameLauncher.RunningPid(path));

            AgentEndpoint.Create(1234, int.MaxValue, "1.0.0").Write(path);
            Assert.IsNull(GameLauncher.RunningPid(path), "a stale file names a dead pid");
        }
    }
}
