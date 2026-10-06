using System.Collections.Concurrent;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
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
        private static readonly ConcurrentDictionary<string, PrototypeId> _lootTableCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Drops <paramref name="count"/> of the item at <paramref name="prototypePath"/> near <paramref name="player"/>'s avatar.
        /// <paramref name="slot"/> spreads drops around the avatar so several rewards do not stack on one spot.
        /// <paramref name="itemLevel"/> sets the item level (0 = the hero's level); the item's own level restrictions still apply.
        /// Returns <see langword="false"/> if the item does not exist or the player has no avatar in the world.
        /// </summary>
        public static bool DropItem(Player player, string prototypePath, int count = 1, int slot = 0, int itemLevel = 0)
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
                ItemSpec itemSpec = game.LootManager.CreateItemSpec(itemRef, LootContext.Drop, player, itemLevel > 0 ? itemLevel : avatar.CharacterLevel);
                if (itemSpec == null)
                    continue;

                summary.Add(new LootResult(itemSpec));
                hasLoot = true;
            }

            return hasLoot && game.LootManager.SpawnLootFromSummary(summary, inputSettings);
        }

        /// <summary>
        /// Rolls the loot table at <paramref name="lootTablePath"/> for <paramref name="player"/> and drops the result near their
        /// avatar, like an enemy kill would (items, credits, orbs and banner messages included). Loot cooldowns are ignored.
        /// Returns <see langword="false"/> if the table does not exist or the player has no avatar in the world.
        /// </summary>
        public static bool DropLootTable(Player player, string lootTablePath, int slot = 0)
        {
            Avatar avatar = player?.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false)
                return false;

            PrototypeId lootTableRef = ResolveLootTable(lootTablePath);
            if (lootTableRef == PrototypeId.Invalid)
                return false;

            Vector3 dropPosition = GetDropPosition(avatar.Region, avatar.RegionLocation.Position, slot);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, avatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            player.Game.LootManager.SpawnLootFromTable(lootTableRef, inputSettings, 1);
            return true;
        }

        // Items scripts made "not droppable" whatever the server's binding setting is
        private static readonly ConcurrentDictionary<PrototypeId, byte> _alwaysBound = new();

        // Items a player threw on the ground (entries vanish with the item): picking one of these up is not a new find
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, object> _playerDropped = new();

        /// <summary>
        /// Makes the item at <paramref name="prototypePath"/> bind to the account when it is picked up, even on a server that
        /// has account binding switched off (DisableAccountBinding). A bound item cannot be traded and is destroyed when the
        /// player drops it, so it can never be picked up twice. Only affects items picked up from now on. Call at script load.
        /// </summary>
        public static bool BindOnPickup(string prototypePath)
        {
            PrototypeId itemRef = ResolveItem(prototypePath);
            if (itemRef == PrototypeId.Invalid)
                return false;

            _alwaysBound[itemRef] = 0;
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> if a script made this item bind on pickup (<see cref="BindOnPickup"/>). Called by the item code.
        /// </summary>
        internal static bool IsAlwaysBound(PrototypeId itemRef)
        {
            return _alwaysBound.IsEmpty == false && _alwaysBound.ContainsKey(itemRef);
        }

        /// <summary>
        /// Remembers that a player dropped <paramref name="item"/> on the ground. Called by the drop code.
        /// </summary>
        internal static void MarkPlayerDropped(Item item)
        {
            if (item != null)
                _playerDropped.AddOrUpdate(item, null);
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="item"/> is lying on the ground because a player dropped it there.
        /// </summary>
        internal static bool WasPlayerDropped(Item item)
        {
            return item != null && _playerDropped.TryGetValue(item, out _);
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="lootTablePath"/> is a loot table prototype.
        /// </summary>
        public static bool IsValidLootTable(string lootTablePath)
        {
            return ResolveLootTable(lootTablePath) != PrototypeId.Invalid;
        }

        // Where a player's spendable items are looked for: backpack and general stash tabs (not equipped gear)
        private const InventoryIterationFlags SpendableInventories =
            InventoryIterationFlags.PlayerGeneral | InventoryIterationFlags.PlayerGeneralExtra | InventoryIterationFlags.PlayerStashGeneral;

        /// <summary>
        /// Returns how many of the item at <paramref name="prototypePath"/> <paramref name="player"/> has in their backpack and
        /// general stash (stack sizes added up).
        /// </summary>
        public static int CountItems(Player player, string prototypePath)
        {
            PrototypeId itemRef = ResolveItem(prototypePath);
            if (player == null || itemRef == PrototypeId.Invalid)
                return 0;

            List<ulong> matches = new();
            InventoryIterator.GetMatchingContained(player, itemRef, SpendableInventories, matches);

            int total = 0;
            foreach (ulong itemId in matches)
            {
                Item item = player.Game.EntityManager.GetEntity<Item>(itemId);
                if (item != null && item.IsDestroyed == false)
                    total += Math.Max(item.CurrentStackSize, 1);
            }

            return total;
        }

        /// <summary>
        /// Takes <paramref name="count"/> of the item at <paramref name="prototypePath"/> from <paramref name="player"/> (e.g. as a
        /// price). Takes nothing and returns <see langword="false"/> if the player does not have that many.
        /// </summary>
        public static bool TakeItems(Player player, string prototypePath, int count)
        {
            PrototypeId itemRef = ResolveItem(prototypePath);
            if (player == null || itemRef == PrototypeId.Invalid || count <= 0)
                return false;

            List<ulong> matches = new();
            InventoryIterator.GetMatchingContained(player, itemRef, SpendableInventories, matches);

            List<Item> items = new();
            int total = 0;
            foreach (ulong itemId in matches)
            {
                Item item = player.Game.EntityManager.GetEntity<Item>(itemId);
                if (item == null || item.IsDestroyed)
                    continue;

                items.Add(item);
                total += Math.Max(item.CurrentStackSize, 1);
            }

            if (total < count)
                return false;

            int remaining = count;
            foreach (Item item in items)
            {
                if (remaining <= 0)
                    break;

                int take = Math.Min(Math.Max(item.CurrentStackSize, 1), remaining);
                if (item.DecrementStack(take))
                    remaining -= take;
            }

            return remaining <= 0;
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

        private static PrototypeId ResolveLootTable(string lootTablePath)
        {
            if (string.IsNullOrWhiteSpace(lootTablePath))
                return PrototypeId.Invalid;

            return _lootTableCache.GetOrAdd(lootTablePath, static path =>
            {
                PrototypeId tableRef = GameDatabase.GetPrototypeRefByName(path);
                if (tableRef == PrototypeId.Invalid || tableRef.As<LootTablePrototype>() == null)
                    return Logger.WarnReturn(PrototypeId.Invalid, $"ResolveLootTable(): [{path}] is not a loot table prototype");

                return tableRef;
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
