using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.Network
{
    public interface IArchiveMessageDispatcher
    {
        public const ulong InvalidReplicationId = 0;

        private static readonly Logger Logger = LogManager.CreateLogger();

        public Game Game { get; }
        public bool CanSendArchiveMessages { get => true; }

        public ulong RegisterMessageHandler<T>(T handler, ref ulong replicationId) where T : IArchiveMessageHandler
        {
            //  If the ID is 0 or taken, keep generating new IDs until we find a completely free one.
            if (replicationId == InvalidReplicationId || Game.MessageHandlerDict.ContainsKey(replicationId))
            {
                do
                {
                    replicationId = Game.CurrentRepId;
                }
                while (Game.MessageHandlerDict.ContainsKey(replicationId));
            }

            // Since the loop above guarantees the ID is unique, we no longer need the secondary warning check.

            //Logger.Debug($"RegisterMessageHandler(): Registered handler id {replicationId} for {this}");
            Game.MessageHandlerDict.Add(replicationId, handler);
            return replicationId;
        }

        public bool UnregisterMessageHandler<T>(T handler) where T : IArchiveMessageHandler
        {
            if (handler.ReplicationId == InvalidReplicationId)
                return false;

            if (Game.MessageHandlerDict.Remove(handler.ReplicationId) == false)
                return Logger.WarnReturn(false, $"UnregisterMessageHandler(): ReplicationId {handler.ReplicationId} not found");

            return true;
        }

        public bool GetInterestedClients(List<PlayerConnection> interestedClientList, AOINetworkPolicyValues interestPolicies);
    }
}
