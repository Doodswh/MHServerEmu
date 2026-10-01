using System.Collections.Concurrent;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Drops reward items for players, the same way Endless Danger Room drops its bonus items. Each drop belongs to the
    /// player it was made for. Call from the game thread that owns the player.
    /// </summary>
    public static class ScriptRewards
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private const float DropMinRadius = 45.0f;
        private const float DropRadiusStep = 16.0f;
        private const float DropMaxRadius = 160.0f;

        private static readonly ConcurrentDictionary<string, PrototypeId> _itemCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Drops <paramref name="count"/> of the item at <paramref name="prototypePath"/> near <paramref name="player"/>'s avatar.
        /// <paramref name="slot"/> spreads drops around the avatar so several rewards do not stack on one spot.
        /// Returns <see langword="false"/> if the item does not exist or the player has no avatar in the world.
        /// </summary>
        public static bool DropItem(Player player, string prototypePath, int count = 1, int slot = 0)
        {
            Avatar avatar = player?.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false || count <= 0)
                return false;

            PrototypeId itemRef = ResolveItem(prototypePath);
            if (itemRef == PrototypeId.Invalid)
                return false;

            Game game = player.Game;
            Vector3 dropPosition = GetDropPosition(avatar.Region, avatar.RegionLocation.Position, slot);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, avatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            using LootResultSummary summary = ObjectPoolManager.Instance.Get<LootResultSummary>();

            bool hasLoot = false;
            for (int i = 0; i < count; i++)
            {
                ItemSpec itemSpec = game.LootManager.CreateItemSpec(itemRef, LootContext.Drop, player);
                if (itemSpec == null)
                    continue;

                summary.Add(new LootResult(itemSpec));
                hasLoot = true;
            }

            return hasLoot && game.LootManager.SpawnLootFromSummary(summary, inputSettings);
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="prototypePath"/> is an item prototype. Useful to validate reward tables.
        /// </summary>
        public static bool IsValidItem(string prototypePath)
        {
            return ResolveItem(prototypePath) != PrototypeId.Invalid;
        }

        private static PrototypeId ResolveItem(string prototypePath)
        {
            if (string.IsNullOrWhiteSpace(prototypePath))
                return PrototypeId.Invalid;

            return _itemCache.GetOrAdd(prototypePath, static path =>
            {
                PrototypeId itemRef = GameDatabase.GetPrototypeRefByName(path);
                if (itemRef == PrototypeId.Invalid || itemRef.As<ItemPrototype>() == null)
                    return Logger.WarnReturn(PrototypeId.Invalid, $"ResolveItem(): [{path}] is not an item prototype");

                return itemRef;
            });
        }

        private static Vector3 GetDropPosition(Region region, Vector3 anchor, int slot)
        {
            if (slot <= 0)
                return RegionLocation.ProjectToFloor(region, anchor);

            const float GoldenAngle = 2.39996323f;
            float radius = Math.Min(DropMaxRadius, DropMinRadius + ((slot - 1) % 8) * DropRadiusStep);
            float angle = slot * GoldenAngle;

            Vector3 candidate = new(anchor.X + MathF.Cos(angle) * radius, anchor.Y + MathF.Sin(angle) * radius, anchor.Z);
            return RegionLocation.ProjectToFloor(region, candidate);
        }
    }
}
