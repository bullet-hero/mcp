using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BH.SDK.Agent;
using BH.SDK.Agent.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BH.MCP.Tests
{
    // Speaks the game's side of the line protocol with a real loopback socket and writes the endpoint
    // file the server reads. It checks the token and the protocol number the way the game does.

    /// <summary> An in-process stand-in for the game's bridge. </summary>
    internal sealed class FakeBridge : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _connections;

        /// <summary> The token the fake accepts. </summary>
        public string Token { get; } = AgentEndpoint.NewToken();

        public int Port { get; }

        public string EndpointPath { get; }

        /// <summary> Answers <c>hello</c> with <c>protocol_mismatch</c>. </summary>
        public bool ReplyProtocolMismatch { get; set; }

        /// <summary> Closes the first connection at its first request after <c>hello</c>. </summary>
        public bool DropFirstConnectionAfterHello { get; set; }

        /// <summary> Every request after <c>hello</c>, in arrival order. </summary>
        public ConcurrentQueue<AgentRequest> Requests { get; } = new ConcurrentQueue<AgentRequest>();

        /// <summary> Answers requests after <c>hello</c> instead of the built-in session answer. </summary>
        public Func<AgentRequest, AgentResponse> Responder { get; set; }

        public int ConnectionCount => Volatile.Read(ref _connections);

        /// <param name="directory"> Where the endpoint file goes. </param>
        /// <param name="endpointToken"> The token written to the file; the fake's own when null. </param>
        public FakeBridge(string directory, string endpointToken = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            EndpointPath = Path.Combine(directory, "endpoint.json");
            new AgentEndpoint { Protocol = AgentProtocol.Version, Port = Port, Token = endpointToken ?? Token, Pid = 1, Gv = "test" }
                .Write(EndpointPath);

            _ = AcceptLoopAsync();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    var index = Interlocked.Increment(ref _connections);
                    _ = ServeAsync(client, index);
                }
            }
            catch (Exception e) when (e is OperationCanceledException || e is SocketException || e is ObjectDisposedException)
            {
            }
        }

        private async Task ServeAsync(TcpClient client, int index)
        {
            try
            {
                using (client)
                using (var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false)))
                using (var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { NewLine = "\n" })
                {
                    var authorised = false;

                    while (!_cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) return;

                        var request = JsonConvert.DeserializeObject<AgentRequest>(line);

                        if (!authorised)
                        {
                            var refusal = CheckHello(request);
                            await WriteAsync(writer, refusal ?? Ok(request, JToken.FromObject(HelloAnswer()))).ConfigureAwait(false);
                            if (refusal != null) return;

                            authorised = true;
                            continue;
                        }

                        if (DropFirstConnectionAfterHello && index == 1) return;

                        Requests.Enqueue(request);
                        await WriteAsync(writer, Responder?.Invoke(request) ?? Answer(request)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
            {
            }
        }

        private AgentResponse CheckHello(AgentRequest request)
        {
            var hello = request.Params?.ToObject<HelloRequest>();

            if (request.Method != AgentProtocol.Methods.Hello || hello == null || hello.Token != Token)
                return Fail(request, AgentErrorCode.BadToken, new AgentErrorData());

            if (ReplyProtocolMismatch || hello.Protocol != AgentProtocol.Version)
            {
                return Fail(request, AgentErrorCode.ProtocolMismatch, new AgentErrorData
                {
                    ServerProtocol = AgentProtocol.Version + 1,
                    ClientProtocol = hello.Protocol,
                });
            }

            return null;
        }

        private static HelloResult HelloAnswer() => new HelloResult
        {
            Protocol = AgentProtocol.Version,
            Gv = "test",
            Sv = "test",
            Mg = 1,
            SessionId = string.Empty,
        };

        private static AgentResponse Answer(AgentRequest request)
        {
            if (request.Method == AgentProtocol.Methods.SessionStatus)
                return Ok(request, JObject.FromObject(new SessionStatusResult { Screen = "menu", SessionId = string.Empty }));

            return Fail(request, AgentErrorCode.NotFound, new AgentErrorData());
        }

        internal static AgentResponse Ok(AgentRequest request, JToken result)
            => new AgentResponse { Id = request.Id, Result = result };

        internal static AgentResponse Fail(AgentRequest request, string code, AgentErrorData data)
        {
            data.Code = code;

            return new AgentResponse
            {
                Id = request.Id,
                Error = new AgentRpcError { Code = AgentProtocol.JsonRpcCode.Application, Message = code, Data = data },
            };
        }

        private static async Task WriteAsync(StreamWriter writer, AgentResponse response)
        {
            await writer.WriteLineAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }
}
