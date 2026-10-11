using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;

namespace BH.MCP
{
    // The server's stdio is the MCP transport, so the game never inherits it: shell execute on Windows,
    // `open` on macOS, sh with every stream sent to /dev/null on Linux. Each start detaches.

    /// <summary> Finds the installed game beside the server, starts it with a launch-argument line, and
    /// waits for its bridge. </summary>
    public sealed class GameLauncher
    {
        /// <summary> How long a start waits for the game to write its endpoint file. </summary>
        public const int EndpointWaitSeconds = 90;

        private const int PollMilliseconds = 500;
        private const string DataSuffix = "_Data";
        private const string LinuxExtension = ".x86_64";
        private const string AppBundleExtension = ".app";

        private const string LinuxStart =
            "if command -v setsid >/dev/null 2>&1; then setsid \"$0\" \"$@\" </dev/null >/dev/null 2>&1 & " +
            "else \"$0\" \"$@\" </dev/null >/dev/null 2>&1 & fi";

        private readonly BridgeClient _bridge;
        private readonly string _overridePath;

        public GameLauncher(BridgeClient bridge, string overridePath)
        {
            _bridge = bridge;
            _overridePath = overridePath;
        }

        /// <summary> The game executable (a bundle on macOS) this server starts, or null. </summary>
        public string Locate() => Locate(AppContext.BaseDirectory, Environment.ProcessPath, _overridePath,
            CurrentPlatform());

        /// <summary> The game beside <paramref name="baseDirectory"/>, laid out as Unity builds a player for
        /// <paramref name="os"/>; <paramref name="overridePath"/> wins when it exists. </summary>
        public static string Locate(string baseDirectory, string ownPath, string overridePath, OSPlatform os)
        {
            if (!string.IsNullOrEmpty(overridePath))
                return File.Exists(overridePath) || Directory.Exists(overridePath) ? overridePath : null;

            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory)) return null;

            if (os == OSPlatform.OSX)
            {
                for (var dir = new DirectoryInfo(baseDirectory); dir != null; dir = dir.Parent)
                    if (dir.Name.EndsWith(AppBundleExtension, StringComparison.OrdinalIgnoreCase))
                        return dir.FullName;

                return null;
            }

            var own = string.IsNullOrEmpty(ownPath) ? null : Path.GetFullPath(ownPath);
            var windows = os == OSPlatform.Windows;
            var candidates = Directory.GetFiles(baseDirectory)
                .Where(f => !string.Equals(Path.GetFullPath(f), own, StringComparison.OrdinalIgnoreCase))
                .Where(f => !windows || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                .Where(f => Directory.Exists(Path.Combine(baseDirectory, StemOf(f, windows) + DataSuffix)))
                .ToArray();

            return candidates.Length == 1 ? candidates[0] : null;
        }

        private static string StemOf(string file, bool windows)
        {
            var name = Path.GetFileName(file);
            if (windows) return Path.GetFileNameWithoutExtension(name);

            return name.EndsWith(LinuxExtension, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - LinuxExtension.Length)
                : name;
        }

        /// <summary> The pid of the game the endpoint file names, when that process is alive; null for no
        /// file or a stale one. </summary>
        public static int? RunningPid(string endpointPath)
        {
            if (!AgentEndpoint.TryRead(endpointPath, out var endpoint, out _)) return null;

            try
            {
                using var process = Process.GetProcessById(endpoint.Pid);
                return process.HasExited ? null : endpoint.Pid;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                return null;
            }
        }

        // The endpoint alone misses a game with MCP off, which writes none; a second start would then open
        // a second game. A process name may be cut short (15 characters on Linux), so it only pre-filters.

        /// <summary> The pid of a live process running <paramref name="game"/> (the binary inside it, for a
        /// macOS bundle), or null. </summary>
        public static int? RunningGamePid(string game, OSPlatform os)
        {
            var executable = os == OSPlatform.OSX ? BundleExecutable(game) : game;
            if (string.IsNullOrEmpty(executable)) return null;

            var full = Path.GetFullPath(executable);
            var name = os == OSPlatform.Windows ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(full);

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var processName = process.ProcessName;
                        if (processName.Length == 0 || !name.StartsWith(processName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var path = process.MainModule?.FileName;
                        if (path != null && string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase))
                            return process.Id;
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException ||
                                              e is NotSupportedException)
                    {
                        // Exited meanwhile, or not ours to inspect.
                    }
                }
            }

            return null;
        }

        private static string BundleExecutable(string bundle)
        {
            var macOs = Path.Combine(bundle, "Contents", "MacOS");
            if (!Directory.Exists(macOs)) return null;

            var files = Directory.GetFiles(macOs);
            return files.Length == 1 ? files[0] : null;
        }

        /// <summary> Starts <paramref name="game"/> with <paramref name="line"/>, detached from this process.
        /// </summary>
        public static void Start(string game, string line, OSPlatform os)
        {
            var info = new ProcessStartInfo();

            if (os == OSPlatform.Windows)
            {
                info.FileName = game;
                info.Arguments = line ?? string.Empty;
                info.UseShellExecute = true;
                info.WorkingDirectory = Path.GetDirectoryName(game) ?? string.Empty;
            }
            else if (os == OSPlatform.OSX)
            {
                info.FileName = "open";
                info.UseShellExecute = false;
                info.ArgumentList.Add("-n");
                info.ArgumentList.Add(game);
                info.ArgumentList.Add("--args");
                foreach (var token in AgentLaunchLine.Split(line)) info.ArgumentList.Add(token);
            }
            else
            {
                info.FileName = "/bin/sh";
                info.UseShellExecute = false;
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(LinuxStart);
                info.ArgumentList.Add(game);
                foreach (var token in AgentLaunchLine.Split(line)) info.ArgumentList.Add(token);
            }

            using var process = Process.Start(info);
        }

        /// <summary> Waits for an endpoint file written at or after <paramref name="sinceUtc"/>; true when one
        /// appeared within <see cref="EndpointWaitSeconds"/>. </summary>
        public async Task<bool> WaitForEndpointAsync(DateTime sinceUtc, CancellationToken token)
        {
            var path = _bridge.EndpointPath;
            var deadline = DateTime.UtcNow.AddSeconds(EndpointWaitSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= sinceUtc.AddSeconds(-1) &&
                    AgentEndpoint.TryRead(path, out _, out _))
                {
                    await _bridge.DisconnectAsync().ConfigureAwait(false);
                    return true;
                }

                await Task.Delay(PollMilliseconds, token).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary> The platform this server runs on, in the terms <see cref="Locate(string, string, string,
        /// OSPlatform)"/> takes. </summary>
        public static OSPlatform CurrentPlatform()
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
                : OSPlatform.Linux;
    }
}
