using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Populations;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    // Hook args expose the real game objects for C# scripts, plus simple value properties (names, ids, flags)
    // that are safe and convenient to use from Lua.

    /// <summary>
    /// Args for <see cref="ScriptHooks.ConditionApplying"/>. Set <see cref="Cancel"/> to prevent the condition from being applied.
    /// </summary>
    public sealed class ConditionApplyingArgs
    {
        public Game Game { get; }
        public WorldEntity Target { get; }
        public ulong PowerOwnerId { get; }
        public PowerPrototype PowerPrototype { get; }
        public ConditionPrototype ConditionPrototype { get; }
        public PropertyCollection ConditionProperties { get; }

        public bool Cancel { get; set; }

        public ulong TargetId { get => Target.Id; }
        public string TargetName { get => Target.PrototypeName; }
        public bool TargetIsAvatar { get => Target is Avatar; }
        public bool IsSelfApplied { get => Target.Id == PowerOwnerId; }
        public string PowerName { get => PowerPrototype != null ? PowerPrototype.DataRef.GetName() : string.Empty; }
        public string ConditionName { get => ConditionPrototype != null ? ConditionPrototype.DataRef.GetName() : string.Empty; }
        public bool IsKnockback { get => ConditionProperties[PropertyEnum.Knockback]; }
        public bool IsKnockdown { get => ConditionProperties[PropertyEnum.Knockdown]; }
        public bool IsKnockup { get => ConditionProperties[PropertyEnum.Knockup]; }
        public bool IsDisplacement { get => IsKnockback || IsKnockdown || IsKnockup; }
        public bool IsStun { get => ConditionProperties[PropertyEnum.Stunned]; }
        public bool IsImmobilize { get => ConditionProperties.HasProperty(PropertyEnum.Immobilized); }
        public string RegionName { get => Target.Region?.PrototypeName ?? string.Empty; }
        public string DifficultyName { get => Target.Region != null ? Target.Region.DifficultyTierRef.GetName() : string.Empty; }
        public bool IsOmega { get => Target.Region != null && Target.Region.IsOmegaDifficulty; }

        public ConditionApplyingArgs(Game game, WorldEntity target, ulong powerOwnerId, PowerPrototype powerProto,
            ConditionPrototype conditionProto, PropertyCollection conditionProperties)
        {
            Game = game;
            Target = target;
            PowerOwnerId = powerOwnerId;
            PowerPrototype = powerProto;
            ConditionPrototype = conditionProto;
            ConditionProperties = conditionProperties;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.EnemyAffixesRolled"/>. The affix list can be modified before it is assigned to the spawn group.
    /// </summary>
    public sealed class EnemyAffixesRolledArgs
    {
        public Region Region { get; }
        public RankPrototype RankPrototype { get; }
        public List<PrototypeId> Affixes { get; }
        public GRandom Random { get; }

        public string RegionName { get => Region?.PrototypeName ?? string.Empty; }
        public string DifficultyName { get => Region != null ? Region.DifficultyTierRef.GetName() : string.Empty; }
        public bool IsOmega { get => Region != null && Region.IsOmegaDifficulty; }
        public string RankName { get => RankPrototype != null ? RankPrototype.DataRef.GetName() : string.Empty; }
        public int Count { get => Affixes.Count(affix => affix != PrototypeId.Invalid); }

        public EnemyAffixesRolledArgs(Region region, RankPrototype rankProto, List<PrototypeId> affixes, GRandom random)
        {
            Region = region;
            RankPrototype = rankProto;
            Affixes = affixes;
            Random = random;
        }

        /// <summary>
        /// Removes all rolled affixes that can knock back, pull, knock down or knock up (detected from game data).
        /// Returns the number removed.
        /// </summary>
        public int RemoveDisplacementAffixes()
        {
            int removed = 0;
            for (int i = 0; i < Affixes.Count; i++)
            {
                if (EnemyBoostCatalog.IsDisplacement(Affixes[i]) == false) continue;

                Affixes[i] = PrototypeId.Invalid;
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Adds up to <paramref name="count"/> random affixes, skipping any whose name contains one of the comma-separated
        /// words in <paramref name="excludeNameContains"/> and any displacement affixes. Returns the number added.
        /// </summary>
        public int AddRandomAffixes(int count, string excludeNameContains)
        {
            return EnemyBoostCatalog.AddRandom(Affixes, Random, count, excludeNameContains, false);
        }

        /// <summary>
        /// Same as <see cref="AddRandomAffixes(int, string)"/>, optionally allowing displacement affixes.
        /// </summary>
        public int AddRandomAffixes(int count, string excludeNameContains, bool allowDisplacement)
        {
            return EnemyBoostCatalog.AddRandom(Affixes, Random, count, excludeNameContains, allowDisplacement);
        }

        /// <summary>
        /// Returns the prototype names of all rolled affixes.
        /// </summary>
        public string[] GetAffixNames()
        {
            return Affixes.Where(affix => affix != PrototypeId.Invalid).Select(affix => affix.GetName()).ToArray();
        }

        /// <summary>
        /// Returns <see langword="true"/> if any rolled affix name contains the provided text (case-insensitive).
        /// </summary>
        public bool HasAffix(string nameContains)
        {
            return Affixes.Any(affix => affix != PrototypeId.Invalid && affix.GetName().Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Removes all rolled affixes whose name contains the provided text (case-insensitive). Returns the number removed.
        /// </summary>
        public int RemoveAffix(string nameContains)
        {
            int removed = 0;
            for (int i = 0; i < Affixes.Count; i++)
            {
                if (Affixes[i] == PrototypeId.Invalid) continue;
                if (Affixes[i].GetName().Contains(nameContains, StringComparison.OrdinalIgnoreCase) == false) continue;

                Affixes[i] = PrototypeId.Invalid;
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Adds an affix by its full prototype path (e.g. Mods/MobAffixes/Normal/Hulking.prototype). Returns <see langword="false"/> if not found.
        /// </summary>
        public bool AddAffix(string prototypePath)
        {
            PrototypeId affixRef = GameDatabase.GetPrototypeRefByName(prototypePath);
            if (affixRef == PrototypeId.Invalid || affixRef.As<EnemyBoostPrototype>() == null)
                return false;

            if (Affixes.Contains(affixRef) == false)
                Affixes.Add(affixRef);

            return true;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.EntityKilled"/>.
    /// </summary>
    public sealed class EntityKilledArgs
    {
        public WorldEntity Victim { get; }
        public WorldEntity Killer { get; }
        public WorldEntity DirectKiller { get; }

        public ulong VictimId { get => Victim.Id; }
        public string VictimName { get => Victim.PrototypeName; }
        public bool VictimIsAvatar { get => Victim is Avatar; }
        public ulong KillerId { get => Killer != null ? Killer.Id : 0; }
        public string KillerName { get => Killer?.PrototypeName ?? string.Empty; }
        public bool KillerIsAvatar { get => Killer is Avatar; }
        public string RegionName { get => Victim.Region?.PrototypeName ?? string.Empty; }
        public bool IsOmega { get => Victim.Region != null && Victim.Region.IsOmegaDifficulty; }

        public EntityKilledArgs(WorldEntity victim, WorldEntity killer, WorldEntity directKiller)
        {
            Victim = victim;
            Killer = killer;
            DirectKiller = directKiller;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.AvatarEnteredWorld"/>.
    /// </summary>
    public sealed class AvatarEnteredWorldArgs
    {
        public Avatar Avatar { get; }
        public Player Player { get; }

        public AvatarEnteredWorldArgs(Avatar avatar, Player player)
        {
            Avatar = avatar;
            Player = player;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.ItemPickedUp"/>.
    /// </summary>
    public sealed class ItemPickedUpArgs
    {
        public Player Player { get; }
        public PrototypeId ItemRef { get; }

        /// <summary>
        /// How many were picked up (the stack size of the item on the ground).
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Full prototype path of the item (Entity/Items/...).
        /// </summary>
        public string ItemPath { get => GameDatabase.GetPrototypeName(ItemRef); }

        /// <summary>
        /// <see langword="true"/> if the item was on the ground because a player dropped it there (not a fresh drop from an
        /// enemy or a reward): counting such pickups would let players count the same item again and again.
        /// </summary>
        public bool WasDroppedByPlayer { get; }

        public ItemPickedUpArgs(Player player, PrototypeId itemRef, int count, bool wasDroppedByPlayer = false)
        {
            Player = player;
            ItemRef = itemRef;
            Count = count;
            WasDroppedByPlayer = wasDroppedByPlayer;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.GlobalEventRequested"/>.
    /// </summary>
    public sealed class GlobalEventRequestedArgs
    {
        public Player Player { get; }
        public PrototypeId EventRef { get; }

        /// <summary>
        /// Full prototype path of the global event (Events/GlobalEvents/Events/...).
        /// </summary>
        public string EventPath { get => GameDatabase.GetPrototypeName(EventRef); }

        public GlobalEventRequestedArgs(Player player, PrototypeId eventRef)
        {
            Player = player;
            EventRef = eventRef;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.PlayerEnteredRegion"/>.
    /// </summary>
    public sealed class PlayerEnteredRegionArgs
    {
        public Player Player { get; }
        public Region Region { get; }

        public string PlayerName { get => Player.GetName(); }
        public ulong PlayerDbId { get => Player.DatabaseUniqueId; }
        public string RegionName { get => Region.PrototypeName; }
        public string DifficultyName { get => Region.DifficultyTierRef.GetName(); }
        public bool IsOmega { get => Region.IsOmegaDifficulty; }

        public PlayerEnteredRegionArgs(Player player, Region region)
        {
            Player = player;
            Region = region;
        }

        /// <summary>
        /// Sends a server chat message to this player.
        /// </summary>
        public void SendMessage(string text)
        {
            ScriptHooks.SendChatMessage(Player, text);
        }

        /// <summary>
        /// Shows registered text (ui.register / ScriptText) as an on-screen banner to this player.
        /// </summary>
        public bool ShowBanner(string key) => ScriptText.ShowBanner(Player, key);
        public bool ShowBanner(string key, int number) => ScriptText.ShowBanner(Player, key, number);
        public bool ShowBanner(string key, int number, string style) => ScriptText.ShowBanner(Player, key, number, style);
        public bool ShowBanner(string key, int number, string style, int durationMS) => ScriptText.ShowBanner(Player, key, number, style, durationMS);
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.EquipmentChanged"/>.
    /// </summary>
    public sealed class EquipmentChangedArgs
    {
        public Agent Owner { get; }
        public Item Item { get; }
        public bool Equipped { get; }

        public bool OwnerIsAvatar { get => Owner is Avatar; }
        public bool OwnerIsInWorld { get => Owner.IsInWorld; }
        public string ItemName { get => Item.PrototypeName; }

        public EquipmentChangedArgs(Agent owner, Item item, bool equipped)
        {
            Owner = owner;
            Item = item;
            Equipped = equipped;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.ChatCommand"/>. "!bossrush stop now" gives Command "bossrush" and Args ["stop", "now"].
    /// </summary>
    public sealed class ChatCommandArgs
    {
        public Player Player { get; }
        public string Command { get; }
        public string[] Args { get; }

        /// <summary>
        /// Set to <see langword="true"/> to consume the message (built-in commands and chat will not see it).
        /// </summary>
        public bool Handled { get; set; }

        public string PlayerName { get => Player.GetName(); }
        public ulong PlayerDbId { get => Player.DatabaseUniqueId; }
        public string RegionName { get => Player.CurrentAvatar?.Region?.PrototypeName ?? string.Empty; }
        public bool IsOmega { get => Player.CurrentAvatar?.Region?.IsOmegaDifficulty == true; }
        public int ArgCount { get => Args.Length; }

        public ChatCommandArgs(Player player, string command, string[] args)
        {
            Player = player;
            Command = command;
            Args = args;
        }

        /// <summary>
        /// Returns the argument at <paramref name="index"/>, or an empty string.
        /// </summary>
        public string GetArg(int index) => index >= 0 && index < Args.Length ? Args[index] : string.Empty;

        /// <summary>
        /// Sends a server chat message to the player who typed the command.
        /// </summary>
        public void Reply(string text) => ScriptHooks.SendChatMessage(Player, text, false);
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.EntityInteracted"/>: a player clicked (talked to / used) a world entity.
    /// Entities spawned with <see cref="ScriptSpawner.SpawnInteractable"/> are clickable; <see cref="ScriptTag"/> is the tag
    /// they were spawned with.
    /// </summary>
    public sealed class EntityInteractedArgs
    {
        public Player Player { get; }
        public Avatar Avatar { get; }
        public WorldEntity Entity { get; }

        public string PlayerName { get => Player.GetName(); }
        public ulong PlayerDbId { get => Player.DatabaseUniqueId; }
        public ulong EntityId { get => Entity.Id; }
        public string EntityName { get => Entity.PrototypeName; }
        public string RegionName { get => Entity.Region?.PrototypeName ?? string.Empty; }
        public bool IsOmega { get => Entity.Region?.IsOmegaDifficulty == true; }

        /// <summary>The tag given to <see cref="ScriptSpawner.SpawnInteractable"/>, or an empty string for game entities.</summary>
        public string ScriptTag { get => ScriptSpawner.GetTag(Entity); }

        public EntityInteractedArgs(Player player, Avatar avatar, WorldEntity entity)
        {
            Player = player;
            Avatar = avatar;
            Entity = entity;
        }

        /// <summary>
        /// Sends a server chat message to the player who clicked.
        /// </summary>
        public void Reply(string text) => ScriptHooks.SendChatMessage(Player, text, false);
    }
}
