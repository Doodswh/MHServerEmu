using MHServerEmu.Core.Config;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Tcp;
using MHServerEmu.Core.RateLimiting;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MHServerEmu.Frontend
{
    /// <summary>
    /// A <see cref="TcpServer"/> that clients connect to.
    /// </summary>
    public class FrontendServer : TcpServer, IGameService
    {
        private new static readonly Logger Logger = LogManager.CreateLogger();

        private readonly ConcurrentDictionary<TcpClientConnection, FrontendClient> _clients = new();
       
        private readonly ConcurrentDictionary<TcpClientConnection, ConcurrentQueue<byte[]>> _preAdmissionBuffers = new();

        private readonly Channel<(TcpClientConnection Connection, DateTime EnqueuedAt)> _loginQueue =
            Channel.CreateBounded<(TcpClientConnection, DateTime)>(
                new BoundedChannelOptions(500)
                {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });

        private readonly TokenBucket _loginRateLimiter = new TokenBucket(5f, 10);

        private static readonly TimeSpan LoginQueueTimeout = TimeSpan.FromSeconds(30);

        private CancellationTokenSource _loginQueueCts;

        public GameServiceState State { get; private set; } = GameServiceState.Created;

        #region IGameService Implementation

        public override void Run()
        {
            var config = ConfigManager.Instance.GetConfig<FrontendConfig>();

            IFrontendClient.FrontendAddress = config.PublicAddress;
            IFrontendClient.FrontendPort = config.Port;

            _receiveTimeoutMS = config.ReceiveTimeoutMS > 0 ? config.ReceiveTimeoutMS : -1;
            _sendTimeoutMS = config.SendTimeoutMS > 0 ? config.SendTimeoutMS : -1;

            if (Start(config.BindIP, int.Parse(config.Port)) == false)
                return;

  
            _loginQueueCts = new CancellationTokenSource();
            _ = Task.Run(() => ProcessLoginQueueAsync(_loginQueueCts.Token));

            Logger.Info($"FrontendServer is listening on {config.BindIP}:{config.Port}...");
            State = GameServiceState.Running;
        }

        public override void Shutdown()
        {
            _loginQueueCts?.Cancel();
            _loginQueue.Writer.TryComplete();

            base.Shutdown();
            State = GameServiceState.Shutdown;
        }

        public void ReceiveServiceMessage<T>(in T message) where T : struct, IGameServiceMessage
        {
            switch (message)
            {
                default:
                    Logger.Warn($"ReceiveServiceMessage(): Unhandled service message type {typeof(T).Name}");
                    break;
            }
        }

        public void GetStatus(Dictionary<string, long> statusDict)
        {
            statusDict["FrontendConnections"] = ConnectionCount;
            statusDict["FrontendClients"] = _clients.Count;
        }

        #endregion

        #region TCP Server Event Handling

        protected override void OnClientConnected(TcpClientConnection connection)
        {
           
            _preAdmissionBuffers.TryAdd(connection, new ConcurrentQueue<byte[]>());

            
            if (!_loginQueue.Writer.TryWrite((connection, DateTime.UtcNow)))
            {
                Logger.Warn($"Login queue is absolutely full. Rejecting connection from {connection}");
                _preAdmissionBuffers.TryRemove(connection, out _);
                connection.Disconnect();
                return;
            }

            Logger.Info($"Connection queued from {connection}. Current queue size: {_loginQueue.Reader.Count}");
        }

        protected override void OnClientDisconnected(TcpClientConnection connection)
        {
  
            _preAdmissionBuffers.TryRemove(connection, out _);

            if (_clients.TryRemove(connection, out var client))
            {
                Logger.Info($"Client [{client}] disconnected");
                client.OnDisconnected();
            }
            else
            {
                Logger.Info($"Queued connection {connection} disconnected before admission.");
            }
        }

        protected override void OnDataReceived(TcpClientConnection connection, byte[] buffer, int length)
        {
            if (_clients.TryGetValue(connection, out var client))
            {
                client.HandleIncomingData(buffer, length);
                return;
            }
            if (_preAdmissionBuffers.TryGetValue(connection, out var queue))
            {
              
                if (queue.Count > 20)
                {
                    Logger.Warn($"Queued client {connection} sent too much early data. Dropping.");
                    connection.Disconnect();
                    return;
                }
  
                byte[] dataCopy = new byte[length];
                Buffer.BlockCopy(buffer, 0, dataCopy, 0, length);
                queue.Enqueue(dataCopy);
            }
        }

        #endregion

        #region Login Queue

        private async Task ProcessLoginQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var entry in _loginQueue.Reader.ReadAllAsync(cancellationToken))
                {
                    if (!entry.Connection.Connected)
                        continue;

                    if (DateTime.UtcNow - entry.EnqueuedAt > LoginQueueTimeout)
                    {
                        Logger.Warn($"Connection from {entry.Connection} timed out in login queue");
                        entry.Connection.Disconnect();
                        continue;
                    }
                    while (!_loginRateLimiter.CheckLimit(1))
                    {
                        await Task.Delay(50, cancellationToken);

                        if (!entry.Connection.Connected)
                            break;
                    }

                    if (!entry.Connection.Connected)
                        continue;

                    try
                    {
                        Logger.Info($"Client admitted from login queue: {entry.Connection}");
                        var client = new FrontendClient(entry.Connection);

                        _clients.TryAdd(entry.Connection, client);

                        if (_preAdmissionBuffers.TryRemove(entry.Connection, out var queue))
                        {
                            while (queue.TryDequeue(out var preData))
                            {
                                client.HandleIncomingData(preData, preData.Length);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error initializing client {entry.Connection}: {ex.Message}");
                        entry.Connection.Disconnect();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Login queue processor shutting down.");
            }
        }

        #endregion
    }
}