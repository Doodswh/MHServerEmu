using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.Network
{
    public interface IArchiveMessageDispatcher
    {
        public const ulong InvalidReplicationId = 0;

        public Game Game { get; }
        public bool CanSendArchiveMessages { get => true; }

        public ulong RegisterMessageHandler<T>(T handler, ref ulong replicationId) where T: IArchiveMessageHandler
        {
            // NOTE: We pass a ref to the replicationId field along with the handler so that we don't have to expose it via a public setter.

            if (replicationId == InvalidReplicationId)
                replicationId = Game.CurrentRepId;

            if (!Verify.IsTrue(Game.MessageHandlers.ContainsKey(replicationId) == false, $"ReplicationId {replicationId} is already used by another message handler"))
                return InvalidReplicationId;

            Game.MessageHandlers.Add(replicationId, handler);
            return replicationId;
        }

        public void UnregisterMessageHandler<T>(T handler) where T: IArchiveMessageHandler
        {
            bool removed = Game.MessageHandlers.Remove(handler.ReplicationId);
            Verify.IsTrue(removed, $"ReplicationId {handler.ReplicationId} not found");
        }

        public bool GetInterestedClients(List<PlayerConnection> interestedClientList, AOINetworkPolicyValues interestPolicies);
    }
}
