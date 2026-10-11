using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace BH.MCP
{
    /// <summary> Reads the lines appended to a file since the last read. A file that shrank (rotated, or
    /// deleted and recreated) is read again from its start; a missing file reads as nothing. </summary>
    public sealed class LogTail
    {
        private readonly string _path;
        private readonly StringBuilder _partial = new StringBuilder();
        private Decoder _decoder = Encoding.UTF8.GetDecoder();
        private long _position;

        public LogTail(string path, long position = 0)
        {
            _path = path;
            _position = position;
        }

        /// <summary> A tail that starts at most <paramref name="backlogBytes"/> before the current end, at a
        /// line start, so a viewer opens on the recent history rather than the whole file. </summary>
        public static LogTail FromEnd(string path, long backlogBytes)
        {
            if (!File.Exists(path)) return new LogTail(path);

            var length = new FileInfo(path).Length;
            var tail = new LogTail(path, Math.Max(0, length - backlogBytes));
            if (tail._position > 0) tail.SkipPartialLine = true;
            return tail;
        }

        private bool SkipPartialLine { get; set; }

        /// <summary> The complete lines added since the last call; a line still being written waits. </summary>
        public List<string> ReadNew()
        {
            var lines = new List<string>();
            if (!File.Exists(_path)) return lines;

            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < _position)
                {
                    _position = 0;
                    _partial.Clear();
                    _decoder = Encoding.UTF8.GetDecoder();
                }

                if (stream.Length == _position) return lines;

                stream.Position = _position;
                var buffer = new byte[stream.Length - _position];
                var read = stream.Read(buffer, 0, buffer.Length);
                _position += read;

                // The decoder keeps a character split across two reads.
                var chars = new char[_decoder.GetCharCount(buffer, 0, read)];
                var count = _decoder.GetChars(buffer, 0, read, chars, 0);
                _partial.Append(chars, 0, count);
            }

            var text = _partial.ToString();
            var last = text.LastIndexOf('\n');
            if (last < 0) return lines;

            _partial.Clear().Append(text, last + 1, text.Length - last - 1);

            foreach (var line in text.Substring(0, last).Split('\n'))
            {
                if (SkipPartialLine)
                {
                    SkipPartialLine = false;
                    continue;
                }

                lines.Add(line.TrimEnd('\r'));
            }

            return lines;
        }
    }

    /// <summary> What a double click on the server shows: what this program is, the client config, and
    /// the live log of every server on this machine. A stdio server started by hand would only wait for
    /// JSON on the keyboard. </summary>
    public static class LogViewer
    {
        private const long BacklogBytes = 16 * 1024;
        private const int PollMs = 250;

        public static int Run(string endpointPath, string version)
        {
            var logPath = ServerLog.PathFor(endpointPath);
            Console.OutputEncoding = Encoding.UTF8;

            foreach (var line in Banner(version, Environment.ProcessPath, endpointPath, logPath))
                Console.WriteLine(line);

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            var tail = LogTail.FromEnd(logPath, BacklogBytes);

            while (!stop.IsCancellationRequested)
            {
                foreach (var line in tail.ReadNew()) Console.WriteLine(line);
                stop.Token.WaitHandle.WaitOne(PollMs);
            }

            return 0;
        }

        /// <summary> The lines printed before the log. </summary>
        public static IEnumerable<string> Banner(string version, string exePath, string endpointPath,
            string logPath)
        {
            yield return $"Bullet Hero MCP {version}";
            yield return string.Empty;
            yield return "This is the MCP server of Bullet Hero. An AI client (Claude, Cursor, ...) starts it itself;";
            yield return "started by hand it only shows the log of the servers the clients started.";
            yield return "Add this to the client's MCP config (Settings > Game Editor > Copy client config gives the same):";
            yield return string.Empty;
            yield return ClientConfig(exePath, endpointPath);
            yield return string.Empty;
            yield return $"Log: {logPath}";
            yield return "Close the window or press Ctrl+C to stop watching.";
            yield return new string('-', 72);
        }

        /// <summary> The one-line client config, the shape the game's Copy client config writes. </summary>
        public static string ClientConfig(string exePath, string endpointPath)
        {
            var server = new JObject
            {
                ["command"] = exePath ?? "mcp",
                ["args"] = new JArray("--endpoint", endpointPath),
            };

            return new JObject { ["mcpServers"] = new JObject { ["bullet-hero"] = server } }
                .ToString(Formatting.None);
        }
    }
}
