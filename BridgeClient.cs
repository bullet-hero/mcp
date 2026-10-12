using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BH.MCP
{
    /// <summary> A failed bridge call: carries the <see cref="AgentErrorCode"/> the AI is told. </summary>
    public sealed class BridgeException : Exception
    {
        /// <summary> One of <see cref="AgentErrorCode"/>. </summary>
        public string Code { get; }

        /// <summary> The error's data (revision, index, reason), or null. </summary>
        public AgentErrorData ErrorData { get; }

        public BridgeException(string code, string message, AgentErrorData data = null) : base(message)
        {
            Code = code;
            ErrorData = data;
        }
    }

    // One connection, opened on the first call and re-opened once when it fails. The endpoint file is
    // read again on every connect: the game writes a fresh token each time it starts listening.

    /// <summary> The server's side of the bridge: a lazy, serial JSON-RPC client over loopback. </summary>
    public sealed class BridgeClient : IDisposable
    {
        /// <summary> What the server calls itself in <c>hello</c>. </summary>
        public const string ClientName = "bullet-hero-mcp";

        private readonly string _endpointPath;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private int? _selectedPid;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private StreamReader _reader;
        private long _nextId;

        /// <summary> What the AI has already been told, so a hint is not repeated every call. </summary>
        public HintFilter Hints { get; } = new HintFilter();

        /// <summary> Where every tool call leaves its one line (<c>BridgeTool</c>); silent until the host
        /// sets it. </summary>
        public ILogger Log { get; set; } = NullLogger.Instance;

        public BridgeClient(string endpointPath) => _endpointPath = endpointPath;

        /// <summary> The default endpoint file: the last game to start writes it. </summary>
        public string EndpointPath => _endpointPath;

        /// <summary> The game every call goes to, or null for the default one. </summary>
        public int? SelectedPid => _selectedPid;

        /// <summary> The endpoint file read on every connect: the selected game's own, or the default. </summary>
        public string TargetPath
            => _selectedPid is { } pid ? AgentEndpoint.InstancePath(_endpointPath, pid) : _endpointPath;

        /// <summary> Points every later call at the game <paramref name="pid"/>, or back at the default for null;
        /// the open connection is dropped. </summary>
        public async Task SelectAsync(int? pid)
        {
            await _gate.WaitAsync().ConfigureAwait(false);

            try
            {
                _selectedPid = pid;
                Close();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary> Drops the open connection, so the next call connects to whatever game listens now.
        /// </summary>
        public async Task DisconnectAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);

            try
            {
                Close();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary> Calls <paramref name="method"/> and returns its result object. </summary>
        /// <exception cref="BridgeException"> The game refused, or could not be reached. </exception>
        public async Task<JToken> CallAsync(string method, object parameters, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (_tcp == null) await ConnectAsync(token).ConfigureAwait(false);
                        return await SendAsync(method, parameters, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // A late answer to the abandoned request must not reach the next one: start afresh.
                        Close();
                        throw;
                    }
                    catch (Exception e) when (IsConnectionLoss(e))
                    {
                        Close();
                        if (attempt >= 1 || token.IsCancellationRequested) throw NotConnected(e.Message);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            Close();
            _gate.Dispose();
        }

        private static bool IsConnectionLoss(Exception e)
            => e is IOException || e is SocketException || e is ObjectDisposedException || e is EndOfStreamException;

        private static BridgeException NotConnected(string detail) => new BridgeException(
            AgentErrorCode.NotConnected,
            "Cannot reach Bullet Hero (" + detail + "). If the player wants it, app_launch starts the game; "
            + "otherwise ask the author to open it and enable MCP (Settings -> Game Editor -> AI Agent (MCP) -> Allow AI Connections), then retry.");

        private async Task ConnectAsync(CancellationToken token)
        {
            var path = TargetPath;
            if (_selectedPid is { } pid && !File.Exists(path))
                throw new BridgeException(AgentErrorCode.NotConnected,
                    $"The selected game (pid {pid}) is gone. instance_list shows the running ones; instance_select "
                    + "with no pid goes back to the default, the last game started.");

            if (!AgentEndpoint.TryRead(path, out var endpoint, out var error)) throw NotConnected(error);

            var tcp = new TcpClient { NoDelay = true };

            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await tcp.ConnectAsync(AgentProtocol.Host, endpoint.Port, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                tcp.Dispose();
                throw NotConnected("the game did not accept the connection");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            _tcp = tcp;
            _stream = tcp.GetStream();
            _reader = new StreamReader(_stream, new UTF8Encoding(false));

            try
            {
                await SendAsync(AgentProtocol.Methods.Hello, new HelloRequest
                {
                    Protocol = AgentProtocol.Version,
                    Token = endpoint.Token,
                    Client = ClientName,
                }, token).ConfigureAwait(false);
            }
            catch
            {
                Close();
                throw;
            }
        }

        private async Task<JToken> SendAsync(string method, object parameters, CancellationToken token)
        {
            var id = ++_nextId;
            var request = new AgentRequest
            {
                Id = new JValue(id),
                Method = method,
                Params = parameters == null ? null : JToken.FromObject(parameters),
            };

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request) + AgentProtocol.LineTerminator);
            await _stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            await _stream.FlushAsync(token).ConfigureAwait(false);

            var line = await ReadLineAsync(token).ConfigureAwait(false);
            var response = JsonConvert.DeserializeObject<AgentResponse>(line)
                           ?? throw new BridgeException(AgentErrorCode.Internal, "The game sent an empty frame.");

            if (response.Error != null) throw ToException(response.Error);
            return response.Result ?? new JObject();
        }

        // A frame is capped by the protocol, but a hostile peer on loopback could send none: read in
        // characters and stop at the byte limit rather than buffering without end.
        private async Task<string> ReadLineAsync(CancellationToken token)
        {
            var builder = new StringBuilder();
            var one = new char[1];

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var read = await _reader.ReadAsync(one.AsMemory(), token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The game closed the connection.");
                if (one[0] == AgentProtocol.LineTerminator) return builder.ToString();

                builder.Append(one[0]);
                if (builder.Length > AgentProtocol.MaxMessageBytes)
                    throw new BridgeException(AgentErrorCode.Internal, "A frame from the game is too long.");
            }
        }

        private static BridgeException ToException(AgentRpcError error)
        {
            var code = error.Data?.Code ?? AgentErrorCode.Internal;
            return new BridgeException(code, error.Message ?? code, error.Data);
        }

        private void Close()
        {
            try { _tcp?.Dispose(); }
            catch (SocketException) { }

            _tcp = null;
            _stream = null;
            _reader = null;
        }
    }
}
