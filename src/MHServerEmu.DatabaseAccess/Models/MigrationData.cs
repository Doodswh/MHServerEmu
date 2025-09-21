using Gazillion;

namespace MHServerEmu.DatabaseAccess.Models
{
    public class MigrationData
    {
        public bool SkipNextUpdate { get; set; }

        public bool IsFirstLoad { get; set; } = true;
        public bool IsVanished { get; set; }

        // Store everything here as ulong, PropertyCollection will sort it out game-side
        public List<KeyValuePair<ulong, ulong>> PlayerProperties { get; } = new(256);
        public List<(ulong, ulong)> WorldView { get; } = new();
        public List<CommunityMemberBroadcast> CommunityStatus { get; } = new();

        // TODO: Summoned inventory

        public MigrationData() { }

        public void Reset()
        {
            SkipNextUpdate = false;

            IsFirstLoad = true;
            IsVanished = false;
            PlayerProperties.Clear();
            WorldView.Clear();
            CommunityStatus.Clear();
        }
    }
}
