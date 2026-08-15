using System.Net;
using System.Net.Sockets;
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

        public void StartTasks(CancellationTokenSource cts)
        {
            // Begin receiving data from our new connection
            _ = Task.Run(async () => await ReceiveAsync(cts));
        }

        /// <summary>
        /// Disconnects this client connection.
        /// </summary>
        public void Disconnect()
        {
            if (Connected)
                _server.DisconnectClient(this);
        }

        // NOTE: We do not return the number of bytes sent in Send() methods because
        // they are meant to use as fire and forget to avoid lagging game instances.

        /// <summary>
        /// Sends a <see cref="byte"/> buffer over this connection.
        /// </summary>
        public void Send(byte[] buffer, int size, SocketFlags flags = SocketFlags.None)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            Task.Run(async () => await SendAsync(buffer, size, flags));
        }

        /// <summary>
        /// Sends an <see cref="IPacket"/> over this connection.
        /// </summary>
        public void Send<T>(T packet, SocketFlags flags = SocketFlags.None) where T: IPacket
        {
            ArgumentNullException.ThrowIfNull(packet);

            Task.Run(async () => await SendAsync(packet, flags));
        }

        /// <summary>
        /// Receives data from a <see cref="TcpClientConnection"/> asynchronously.
        /// </summary>
        private async Task ReceiveAsync(CancellationTokenSource cts)
        {
            while (true)
            {
                try
                {
                    Task<int> receiveTask = Socket.ReceiveAsync(_receiveBuffer, SocketFlags.None);
                    await Task.WhenAny(receiveTask, Task.Delay(_server.ReceiveTimeoutMS, cts.Token));

                    if (cts.Token.IsCancellationRequested)
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

        /// <summary>
        /// Sends a <see cref="byte"/> buffer over the provided <see cref="TcpClientConnection"/> asynchronously.
        /// Return the number of bytes sent.
        /// </summary>
        private async ValueTask<int> SendAsync(byte[] buffer, int size, SocketFlags flags)
        {
            int bytesSentTotal = 0;
            int bytesRemaining = size;

            try
            {
                while (bytesRemaining > 0)      // Send all bytes from our buffer
                {
                    ReadOnlyMemory<byte> bytes = buffer.AsMemory(bytesSentTotal, bytesRemaining);
                    int bytesSent = await Socket.SendAsync(bytes, flags);
                    bytesRemaining -= bytesSent;
                    bytesSentTotal += bytesSent;
                }
            }
            catch (SocketException)
            {
                _server.DisconnectClient(this);
            }
            catch (Exception e)
            {
                Logger.ErrorException(e, nameof(SendAsync));
            }

            return bytesSentTotal;
        }

        /// <summary>
        /// Sends an <see cref="IPacket"/> over the provided <see cref="TcpClientConnection"/> asynchronously.
        /// Returns the number of bytes sent.
        /// </summary>
        private async ValueTask<int> SendAsync<T>(T packet, SocketFlags flags = SocketFlags.None) where T : IPacket
        {
            int sent = 0;

            int size = packet.SerializedSize;
            byte[] buffer = _server.BufferPool.Rent(size);

            try
            {
                packet.Serialize(buffer, 0);
                sent = await SendAsync(buffer, size, flags);
            }
            finally
            {
                _server.BufferPool.Return(buffer);
                packet.Dispose();
            }

            return sent;
        }
    }
}
