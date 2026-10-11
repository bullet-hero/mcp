using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BH.MCP
{
    /// <summary> Appends every log line to one file, shared with any other server running at the same time
    /// (each line carries its process id). A file that cannot be opened turns the provider into a no-op:
    /// logging must never stop the server. </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new object();
        private readonly LogLevel _minimum;
        private readonly int _pid = Environment.ProcessId;
        private StreamWriter _writer;

        public FileLoggerProvider(string path, LogLevel minimum)
        {
            _minimum = minimum;
            Path = path;

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
                ServerLog.Rotate(path);

                var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.WriteLine($"BH.MCP: no log file at {path}: {e.Message}");
                _writer = null;
            }
        }

        /// <summary> The file written to. </summary>
        public string Path { get; }

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void Write(LogLevel level, string category, string message)
        {
            lock (_gate)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(ServerLog.Line(DateTime.Now, _pid, level, category, message));
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _owner;
            private readonly string _category;

            public FileLogger(FileLoggerProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _owner._minimum;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var message = formatter(state, exception);
                if (exception != null) message += " | " + exception.GetType().Name + ": " + exception.Message;
                _owner.Write(logLevel, _category, message);
            }
        }
    }
}
