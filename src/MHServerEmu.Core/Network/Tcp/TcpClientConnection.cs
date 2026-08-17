using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using MHServerEmu.Core.Config;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Core.Network.Tcp
{
    /// <summary>
    /// A wrapper around <see cref="System.Net.Sockets.Socket"/> that represents a TCP server's connection to a client.
    /// </summary>
    public class TcpClientConnection
    {
        public const int ReceiveBufferSize = 1024 * 8;     // 8 KB, client input should be relatively small
        public const int SendBufferSize = 1024 * 512;      // 512 KB, enough to fit region loading packets + extra

        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly bool HideSensitiveInformation = ConfigManager.Instance.GetConfig<LoggingConfig>().HideSensitiveInformation;

        private readonly TcpServer _server;
        private readonly byte[] _receiveBuffer;

        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<IPacket> _sendChannel = Channel.CreateUnbounded<IPacket>();

        public Socket Socket { get; }
        public bool Connected { get => Socket.Connected; }
        public IPEndPoint RemoteEndPoint { get => (IPEndPoint)Socket.RemoteEndPoint; }

        public TcpClient Client { get; internal set; }
        public bool IsReceiveTimeoutSuspended { get; set; }

        /// <summary>
        /// Constructs a new client connection instance.
        /// </summary>
        public TcpClientConnection(TcpServer server, Socket socket)
        {
            _server = server;
            _receiveBuffer = new byte[ReceiveBufferSize];   // TODO: reuse receive buffers

            socket.SendTimeout = _server.SendTimeoutMS;
            socket.SendBufferSize = SendBufferSize;
            Socket = socket;
        }

        public override string ToString()
        {
            if (RemoteEndPoint == null)
                return "NULL";

            if (HideSensitiveInformation)
                return $"0x{HashHelper.Djb2(RemoteEndPoint.Address.ToString()):X8}";

            return RemoteEndPoint.ToString();
        }

        /// <summary>
        /// Disconnects this client connection.
        /// </summary>
        public void Disconnect()
        {
            if (Connected)
                _server.DisconnectClient(this);
        }

        /// <summary>
        /// Sends an <see cref="IPacket"/> over this connection.
        /// </summary>
        /// <remarks>
        /// We do not return the number of bytes because this is meant to be used
        /// as fire and forget to avoid lagging game instances.
        /// </remarks>
        public void Send<T>(T packet, SocketFlags flags = SocketFlags.None) where T: IPacket
        {
            ArgumentNullException.ThrowIfNull(packet);

            _sendChannel.Writer.TryWrite(packet);
        }

        internal void StartAsyncTasks()
        {
            _ = Task.Run(ReceiveAsync);
            _ = Task.Run(SendAsync);
        }

        internal void StopAsyncTasks()
        {
            _cts.Cancel();
        }

        /// <summary>
        /// Receives data from a <see cref="TcpClientConnection"/> asynchronously.
        /// </summary>
        private async Task ReceiveAsync()
        {
            while (_cts.IsCancellationRequested == false)
            {
                try
                {
                    Task<int> receiveTask = Socket.ReceiveAsync(_receiveBuffer, SocketFlags.None);
                    await Task.WhenAny(receiveTask, Task.Delay(_server.ReceiveTimeoutMS, _cts.Token));

                    if (_cts.Token.IsCancellationRequested)
                        return;

                    if (IsReceiveTimeoutSuspended == false && receiveTask.IsCompleted == false)
                        throw new TimeoutException();

                    int bytesReceived = await receiveTask;

                    if (bytesReceived == 0)             // Connection lost
                        break;

                    IsReceiveTimeoutSuspended = false;

                    // Do the OnDataReceived() callback to parse received data from the connection's buffer.
                    Client.OnDataReceived(_receiveBuffer, bytesReceived);

                    if (Connected == false)  // Stop receiving if no longer connected
                        break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    Logger.Warn($"ReceiveDataAsync(): Connection to {this} timed out");
                    break;
                }
                catch (Exception e)
                {
                    Logger.ErrorException(e, nameof(ReceiveAsync));
                    break;
                }
            }

            _server.DisconnectClient(this);
        }

        private async Task SendAsync()
        {
            while (_cts.IsCancellationRequested == false)
            {
                try
                {
                    IPacket packet = await _sendChannel.Reader.ReadAsync(_cts.Token);
                    await SendPacketAsync(packet);
                }
                catch (SocketException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Logger.ErrorException(e, nameof(SendAsync));
                    break;
                }
            }

            _server.DisconnectClient(this);
        }

        /// <summary>
        /// Sends an <see cref="IPacket"/> over the provided <see cref="TcpClientConnection"/> asynchronously.
        /// Returns the number of bytes sent.
        /// </summary>
        private async ValueTask<int> SendPacketAsync<T>(T packet, SocketFlags flags = SocketFlags.None) where T : IPacket
        {
            int totalSent = 0;

            byte[] buffer = _server.BufferPool.Rent(packet.SerializedSize);

            try
            {
                int bytesRemaining = packet.Serialize(buffer, 0);

                while (bytesRemaining > 0)
                {
                    ReadOnlyMemory<byte> bytes = buffer.AsMemory(totalSent, bytesRemaining);
                    int sent = await Socket.SendAsync(bytes, flags, _cts.Token);
                    bytesRemaining -= sent;
                    totalSent += sent;
                }
            }
            finally
            {
                _server.BufferPool.Return(buffer);
                packet.Dispose();
            }

            return totalSent;
        }
    }
}
