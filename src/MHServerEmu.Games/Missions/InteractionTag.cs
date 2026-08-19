using Gazillion;
using MHServerEmu.Core.Memory;

namespace MHServerEmu.Games.Missions
{
    public readonly struct InteractionTag
    {
        public ulong EntityId { get; }
        public ulong RegionId { get; }
        //public TimeSpan Timestamp { get; }     // GameTime, used only in non-replication archives, tags older than 1 day are discarded during deserialization

        public InteractionTag(ulong entityId, ulong regionId)
        {
            EntityId = entityId;
            RegionId = regionId;
            //Timestamp = TimeSpan.Zero;
        }

        public override string ToString()
        {
            return $"{nameof(EntityId)}={EntityId}, {nameof(RegionId)}={RegionId}";
        }

        public NetStructMissionInteractionTag ToProtobuf()
        {
            using var builderHandle = ProtobufBuilderPool<NetStructMissionInteractionTag.Builder>.Get(out var builder);
            return builder.SetEntityId(EntityId).SetRegionId(RegionId).Build();
        }
    }
}
