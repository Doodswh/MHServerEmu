namespace MHServerEmu.Core.Network.Tcp
{
    /// <summary>
    /// Base class for <see cref="TcpServer"/> clients.
    /// </summary>
    public abstract class TcpClient
    {
        public TcpClientConnection Connection { get; }

        public TcpClient(TcpClientConnection connection)
        {
            Connection = connection;
            Connection.Client = this;
        }

        /// <summary>
        /// Raised when the server receives data from a client connection.
        /// </summary>
        public abstract void OnDataReceived(byte[] buffer, int length);
    }
}
