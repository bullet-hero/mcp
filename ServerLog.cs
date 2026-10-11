using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BH.MCP
{
    /// <summary> Where the server's log file lives and how one of its lines reads. The file sits beside the
    /// endpoint file, so a server and the viewer that a double click opens find it without being told. </summary>
    public static class ServerLog
    {
        public const string FolderName = "logs";
        public const string FileName = "server.log";
        public const string PreviousFileName = "server.1.log";

        /// <summary> Past this size the file is moved to <see cref="PreviousFileName"/> when a server starts.
        /// </summary>
        public const long MaxBytes = 2L * 1024 * 1024;

        /// <summary> The log file for a server reading <paramref name="endpointPath"/>. </summary>
        public static string PathFor(string endpointPath)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(endpointPath)) ?? ".";
            return Path.Combine(folder, FolderName, FileName);
        }

        /// <summary> Moves an oversized log aside, keeping one previous file. Several servers may start at
        /// once, so losing the race is not an error. </summary>
        public static void Rotate(string path, long maxBytes = MaxBytes)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= maxBytes) return;

                var previous = Path.Combine(file.DirectoryName ?? ".", PreviousFileName);
                File.Move(path, previous, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary> One line: local time, process id, level, category, message. </summary>
        public static string Line(DateTime time, int pid, LogLevel level, string category, string message)
        {
            var text = new StringBuilder(64 + (message?.Length ?? 0));
            text.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(pid.ToString(CultureInfo.InvariantCulture)).Append("] ")
                .Append(Level(level)).Append(' ')
                .Append(ShortCategory(category)).Append(": ")
                .Append((message ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " | "));
            return text.ToString();
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none",
        };

        private static string ShortCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return "-";

            var dot = category.LastIndexOf('.');
            return dot < 0 ? category : category.Substring(dot + 1);
        }
    }
}
