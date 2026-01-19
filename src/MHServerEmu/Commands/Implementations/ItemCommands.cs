using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Serialization;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Loot.Specs;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Properties;
using System;
using System.Buffers.Text;
using System.Collections.Generic; // Needed for List
using System.Diagnostics;
using System.Linq; // Needed for ToList
using System.Text;
using static MHServerEmu.Games.Entities.Inventories.Inventory;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("item")]
    [CommandGroupDescription("Commands for managing items.")]
    public class ItemCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        [Command("drop")]
        [CommandDescription("Creates and drops the specified item from the current avatar.")]
        [CommandUsage("item drop [pattern] [count]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Drop(string[] @params, NetClient client)
        {
            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, @params[0], client);
            if (itemProtoRef == PrototypeId.Invalid) return string.Empty;

            if (@params.Length == 1 || int.TryParse(@params[1], out int count) == false)
                count = 1;

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            LootManager lootManager = playerConnection.Game.LootManager;

            for (int i = 0; i < count; i++)
            {
                lootManager.SpawnItem(itemProtoRef, LootContext.Drop, player, avatar);
                Logger.Debug($"DropItem(): {itemProtoRef.GetName()} from {avatar}");
            }

            return string.Empty;
        }


        [Command("craft")]
        [CommandDescription("Creates a max-level item with max grade")]
        [CommandUsage("item craft [item_pattern] [runeword=random] [blessing=random] [grade=80]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Craft(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            LootManager lootManager = player.Game.LootManager;


            string itemPattern = @params[0];
            string runewordPattern = @params.Length > 1 ? @params[1] : "random";
            string blessingPattern = @params.Length > 2 ? @params[2] : "random";
            int grade = 80;
            if (@params.Length > 3 && !int.TryParse(@params[3], out grade))
            {
                return "Error: Invalid grade specified. Must be a number.";
            }
            grade = Math.Clamp(grade, 0, 80);

            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, itemPattern, client);
            if (itemProtoRef == PrototypeId.Invalid)
                return $"Error: Item prototype not found for '{itemPattern}'.";

            ItemPrototype itemProto = itemProtoRef.As<ItemPrototype>();
            if (itemProto == null)
                return "Error: Invalid item prototype.";

            int itemLevel = 63;
            PrototypeId rarityProtoRef = GameDatabase.LootGlobalsPrototype.RarityCosmic;
            if (rarityProtoRef == PrototypeId.Invalid)
            {
                rarityProtoRef = GameDatabase.LootGlobalsPrototype.RarityDefault;
            }

            List<AffixSpec> affixSpecs = new List<AffixSpec>();
            using DropFilterArguments filterArgs = ObjectPoolManager.Instance.Get<DropFilterArguments>();
            filterArgs.ItemProto = itemProto;
            filterArgs.Level = itemLevel;
            filterArgs.Rarity = rarityProtoRef;
            Avatar currentAvatar = player.CurrentAvatar;
            AgentPrototype currentAvatarProto = currentAvatar?.AvatarPrototype;
            filterArgs.Slot = itemProto.GetInventorySlotForAgent(currentAvatarProto);
            filterArgs.RollFor = currentAvatarProto?.DataRef ?? PrototypeId.Invalid;

            var random = new System.Random(player.Game.Random.Next());

            if (rarityProtoRef == GameDatabase.LootGlobalsPrototype.RarityCosmic)
            {
                int cosmicCount = 0;
                foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                {
                    AffixPrototype ap = affixId.As<AffixPrototype>();
                    if (ap == null) continue;

                    bool isCosmicPosition = (ap.Position != AffixPosition.Prefix &&
                                             ap.Position != AffixPosition.Suffix &&
                                             ap.Position != AffixPosition.Runeword &&
                                             ap.Position != AffixPosition.Blessing);

                    if (isCosmicPosition &&
                        GameDatabase.GetPrototypeName(affixId).Contains("Cosmic", StringComparison.OrdinalIgnoreCase) &&
                        ap.AllowAttachment(filterArgs))
                    {
                        affixSpecs.Add(new AffixSpec(ap, PrototypeId.Invalid, player.Game.Random.Next()));
                        cosmicCount++;
                    }
                }
                Logger.Info($"Craft: Forced {cosmicCount} Cosmic affixes onto item '{itemPattern}'.");
            }

            if (!string.IsNullOrEmpty(runewordPattern) && !runewordPattern.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                AffixPrototype runewordAffix = null;
                if (runewordPattern.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    List<AffixPrototype> validRunewords = new List<AffixPrototype>();
                    foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                    {
                        AffixPrototype proto = affixId.As<AffixPrototype>();
                        if (proto != null && proto.Position == AffixPosition.Runeword && proto.AllowAttachment(filterArgs))
                            validRunewords.Add(proto);
                    }
                    if (validRunewords.Count > 0) runewordAffix = validRunewords[random.Next(validRunewords.Count)];
                }
                else
                {
                    runewordAffix = FindAffix(runewordPattern, AffixPosition.Runeword, filterArgs);
                    if (runewordAffix == null) return $"Error: Could not find valid runeword matching '{runewordPattern}'.";
                }

                if (runewordAffix != null) affixSpecs.Add(new AffixSpec(runewordAffix, PrototypeId.Invalid, player.Game.Random.Next()));
            }

            if (!string.IsNullOrEmpty(blessingPattern) && !blessingPattern.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                AffixPrototype blessingAffix = null;
                if (blessingPattern.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    List<AffixPrototype> validBlessings = new List<AffixPrototype>();
                    foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                    {
                        AffixPrototype proto = affixId.As<AffixPrototype>();
                        if (proto != null && proto.Position == AffixPosition.Blessing && proto.AllowAttachment(filterArgs))
                            validBlessings.Add(proto);
                    }
                    if (validBlessings.Count > 0) blessingAffix = validBlessings[random.Next(validBlessings.Count)];
                }
                else
                {
                    blessingAffix = FindAffix(blessingPattern, AffixPosition.Blessing, filterArgs);
                    if (blessingAffix == null) return $"Error: Could not find valid blessing matching '{blessingPattern}'.";
                }

                if (blessingAffix != null) affixSpecs.Add(new AffixSpec(blessingAffix, PrototypeId.Invalid, player.Game.Random.Next()));
            }


            int numPrefixes = 17;
            int numSuffixes = 17;

            List<AffixPrototype> validPrefixes = new List<AffixPrototype>();
            List<AffixPrototype> validSuffixes = new List<AffixPrototype>();

            foreach (AffixPrototype affix in GetAllBonusAffixes())
            {

                string affixName = GameDatabase.GetPrototypeName(affix.DataRef);
                bool isGarbageAffix = (affixName.Contains("Loot", StringComparison.OrdinalIgnoreCase) &&
                                       affixName.Contains("Debug", StringComparison.OrdinalIgnoreCase))
                                       || affixName.Contains("Test", StringComparison.OrdinalIgnoreCase);

                if (isGarbageAffix)
                {
                    continue; 
                }
               

                if (affix.AllowAttachment(filterArgs))
                {
                    if (affix.Position == AffixPosition.Prefix) validPrefixes.Add(affix);
                    else if (affix.Position == AffixPosition.Suffix) validSuffixes.Add(affix);
                }
            }


            var randomForShuffle = new System.Random(player.Game.Random.Next());
            int n = validPrefixes.Count;
            while (n > 1) { n--; int k = randomForShuffle.Next(n + 1); (validPrefixes[k], validPrefixes[n]) = (validPrefixes[n], validPrefixes[k]); }

            n = validSuffixes.Count;
            while (n > 1) { n--; int k = randomForShuffle.Next(n + 1); (validSuffixes[k], validSuffixes[n]) = (validSuffixes[n], validSuffixes[k]); }

            int actualPrefixesAdded = 0;
            for (int i = 0; i < validPrefixes.Count && actualPrefixesAdded < numPrefixes; i++)
            {
                affixSpecs.Add(new AffixSpec(validPrefixes[i], PrototypeId.Invalid, player.Game.Random.Next()));
                actualPrefixesAdded++;
            }

            int actualSuffixesAdded = 0;
            for (int i = 0; i < validSuffixes.Count && actualSuffixesAdded < numSuffixes; i++)
            {
                affixSpecs.Add(new AffixSpec(validSuffixes[i], PrototypeId.Invalid, player.Game.Random.Next()));
                actualSuffixesAdded++;
            }

            ItemSpec finalSpec = new ItemSpec(itemProtoRef, rarityProtoRef, itemLevel, 0, affixSpecs, player.Game.Random.Next());

            using (LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>())
            {
                lootResultSummary.Add(new LootResult(finalSpec));

                Item createdItem = lootManager.CreateAndGiveItem(finalSpec, player);

                if (createdItem == null)
                    return "Error: Failed to create or give the item.";

                if (grade > 0 && createdItem.Prototype is LegendaryPrototype)
                {
                    long totalXpNeeded = 0;
                    for (int i = 0; i < grade; i++)
                    {
                        totalXpNeeded += GameDatabase.AdvancementGlobalsPrototype.GetItemAffixLevelUpXPRequirement(i);
                    }
                    createdItem.AwardAffixXP(totalXpNeeded);
                }

                StringBuilder result = new StringBuilder();
                result.AppendLine($"Successfully crafted: {GameDatabase.GetPrototypeName(itemProtoRef)}");
                result.AppendLine($"Level: {itemLevel} | Rarity: Cosmic");
                result.AppendLine($"Affixes: {affixSpecs.Count} total (Prefixes: {actualPrefixesAdded}, Suffixes: {actualSuffixesAdded})");
                if (grade > 0 && createdItem.Prototype is LegendaryPrototype)
                    result.AppendLine($"Grade: {grade}/80 (MAX)");

                return result.ToString();
            }
        }


        private AffixPrototype FindAffix(string pattern, AffixPosition position, DropFilterArguments filterArgs)
        {
            foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                AffixPrototype proto = affixId.As<AffixPrototype>();
                if (proto != null &&
                    proto.Position == position &&
                    GameDatabase.GetPrototypeName(affixId).IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    proto.AllowAttachment(filterArgs))
                {
                    return proto;
                }
            }
            return null;
        }
        private List<AffixPrototype> GetAllBonusAffixes()
        {
            List<AffixPrototype> affixes = new List<AffixPrototype>();
            foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                AffixPrototype currentAffix = affixId.As<AffixPrototype>();
                if (currentAffix != null && currentAffix.HasBonusPropertiesToApply)
                {
                    affixes.Add(currentAffix);
                }
            }
            return affixes;
        }


        [Command("give")]
        [CommandDescription("Creates and gives the specified item to the current player.")]
        [CommandUsage("item give [pattern] [count]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Give(string[] @params, NetClient client)
        {
            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, @params[0], client);
            if (itemProtoRef == PrototypeId.Invalid) return string.Empty;

            if (@params.Length == 1 || int.TryParse(@params[1], out int count) == false)
                count = 1;

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            LootManager lootGenerator = playerConnection.Game.LootManager;

            for (int i = 0; i < count; i++)
                lootGenerator.GiveItem(itemProtoRef, LootContext.Drop, player);
            Logger.Debug($"GiveItem(): {itemProtoRef.GetName()}[{count}] to {player}");

            return string.Empty;
        }
        [Command("givemaxlevel")]
        [CommandDescription("Gives the specified item at max level (63) with standard affix rolling.")]
        [CommandUsage("item givemaxlevel [pattern]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GiveMaxLevel(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            LootManager lootManager = playerConnection.Game.LootManager;

            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, @params[0], client);
            if (itemProtoRef == PrototypeId.Invalid)
            {
                Logger.Warn($"GiveMaxLevel: Could not find item prototype for pattern '{@params[0]}'.");
                return "Error: Item prototype not found.";
            }

            int targetLevel = 69;
            ItemSpec itemSpec = lootManager.CreateItemSpec(itemProtoRef, LootContext.Drop, player, targetLevel);

            if (itemSpec == null || !itemSpec.IsValid)
            {
                Logger.Warn($"GiveMaxLevel: Failed to create a valid ItemSpec for {GameDatabase.GetPrototypeName(itemProtoRef)} at level {targetLevel}.");
                return $"Error: Could not create item spec for {GameDatabase.GetPrototypeName(itemProtoRef)} at level {targetLevel}.";
            }
            itemSpec.StackCount = 1;

            using (LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>())
            {
                lootResultSummary.Add(new LootResult(itemSpec));
                if (lootManager.GiveLootFromSummary(lootResultSummary, player))
                {
                    Logger.Info($"GiveMaxLevel: Successfully gave {GameDatabase.GetPrototypeName(itemProtoRef)} (Lvl {targetLevel}, standard affixes) to {player.GetName()}");
                    return $"Successfully gave {GameDatabase.GetPrototypeName(itemProtoRef)} at level {targetLevel} with standard affixes.";
                }
                else
                {
                    Logger.Error($"GiveMaxLevel: Failed to give item {GameDatabase.GetPrototypeName(itemProtoRef)} to {player.GetName()}.");
                    return "Error: Failed to give item after creating spec.";
                }
            }
        }

        [Command("givemaxaffixes")]
        [CommandDescription("Gives item at max level (63) with a full set of deterministically chosen (first valid) prefixes & suffixes, plus built-ins.")]
        [CommandUsage("item givemaxaffixes [pattern]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GiveMaxAffixes(string[] @params, NetClient client)
        {
            return CreateAndGiveItemWithExplicitPrefixSuffix(@params[0], client, AffixSelectionStrategy.FirstValid, new string[0]);
        }

        [Command("givemaxaffixesrandom")]
        [CommandDescription("Gives item at max level (63) with a full set of randomly chosen valid prefixes & suffixes, plus built-ins.")]
        [CommandUsage("item givemaxaffixesrandom [pattern]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GiveMaxAffixesRandom(string[] @params, NetClient client)
        {
            return CreateAndGiveItemWithExplicitPrefixSuffix(@params[0], client, AffixSelectionStrategy.RandomValid, new string[0]);
        }

        [Command("givewithaffixes")]
        [CommandDescription("Gives item at max level (63) with a set of specified affixes.")]
        [CommandUsage("item givewithaffixes [item_pattern] [affix1_pattern] [affix2_pattern] ...")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(2)]
        public string GiveWithAffixes(string[] @params, NetClient client)
        {
            string itemPattern = @params[0];
            string[] affixPatterns = @params.Skip(1).ToArray();
            return CreateAndGiveItemWithExplicitPrefixSuffix(itemPattern, client, AffixSelectionStrategy.Specified, affixPatterns);
        }

        private enum AffixSelectionStrategy { FirstValid, RandomValid, Specified }

        private string CreateAndGiveItemWithExplicitPrefixSuffix(string itemPattern, NetClient client, AffixSelectionStrategy selectionStrategy, string[] specifiedAffixPatterns)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            LootManager lootManager = playerConnection.Game.LootManager;

            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, itemPattern, client);
            if (itemProtoRef == PrototypeId.Invalid)
            {
                Logger.Warn($"CreateAndGiveItemWithExplicitPrefixSuffix: Could not find item prototype for pattern '{itemPattern}'.");
                return "Error: Item prototype not found.";
            }

            ItemPrototype itemProto = itemProtoRef.As<ItemPrototype>();
            if (itemProto == null)
            {
                Logger.Warn($"CreateAndGiveItemWithExplicitPrefixSuffix: Could not cast {GameDatabase.GetPrototypeName(itemProtoRef)} to ItemPrototype.");
                return "Error: Invalid item prototype.";
            }

            int maxItemLevel = 63;

            PrototypeId cosmicRarityProtoRef = GameDatabase.LootGlobalsPrototype.RarityCosmic;
            RarityPrototype chosenRarityProto = cosmicRarityProtoRef.As<RarityPrototype>();
            if (chosenRarityProto == null)
            {
                chosenRarityProto = GameDatabase.LootGlobalsPrototype.RarityDefault.As<RarityPrototype>();
                if (chosenRarityProto == null) { Logger.Error("CreateAndGiveItemWithExplicitPrefixSuffix: Rarity Nof Found"); return "Error: Rarity not found."; }
                Logger.Warn("CreateAndGiveItemWithExplicitPrefixSuffix: Cosmic rarity not found, falling back to default.");
            }

            List<AffixSpec> newAffixSpecs = new List<AffixSpec>();

            using DropFilterArguments filterArgs = ObjectPoolManager.Instance.Get<DropFilterArguments>();
            filterArgs.ItemProto = itemProto;
            filterArgs.Level = maxItemLevel;
            filterArgs.Rarity = chosenRarityProto.DataRef;
            Avatar currentAvatar = player.CurrentAvatar;
            AgentPrototype currentAvatarProto = currentAvatar?.AvatarPrototype;
            filterArgs.Slot = itemProto.GetInventorySlotForAgent(currentAvatarProto);
            filterArgs.RollFor = currentAvatarProto?.DataRef ?? PrototypeId.Invalid;

            if (selectionStrategy == AffixSelectionStrategy.Specified)
            {
                foreach (string affixPattern in specifiedAffixPatterns)
                {
                    // Since there is no Affix.blueprint, we iterate through all AffixPrototypes manually.
                    List<PrototypeId> matchingAffixes = new();
                    foreach (PrototypeId affixId in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                    {
                        if (GameDatabase.GetPrototypeName(affixId).IndexOf(affixPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matchingAffixes.Add(affixId);
                        }
                    }

                    PrototypeId affixProtoRef;
                    if (matchingAffixes.Count == 0)
                    {
                        return $"Error: No affix prototype found for pattern '{affixPattern}'.";
                    }
                    else if (matchingAffixes.Count > 1)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine($"Error: Multiple affixes found for pattern '{affixPattern}'. Please be more specific. Matches:");
                        foreach (var id in matchingAffixes.Take(5)) // Limit to 5 to avoid spam
                        {
                            sb.AppendLine($"- {GameDatabase.GetPrototypeName(id)}");
                        }
                        if (matchingAffixes.Count > 5)
                        {
                            sb.AppendLine($"...and {matchingAffixes.Count - 5} more.");
                        }
                        return sb.ToString();
                    }
                    else
                    {
                        affixProtoRef = matchingAffixes[0];
                    }

                    AffixPrototype affixProto = affixProtoRef.As<AffixPrototype>();
                    if (affixProto.AllowAttachment(filterArgs))
                    {
                        newAffixSpecs.Add(new AffixSpec(affixProto, PrototypeId.Invalid, player.Game.Random.Next()));
                    }
                    else
                    {
                        return $"Error: Affix '{GameDatabase.GetPrototypeName(affixProtoRef)}' cannot be attached to item '{itemPattern}'.";
                    }
                }
            }
            else
            {
                AffixLimitsPrototype affixLimits = itemProto.GetAffixLimits(chosenRarityProto.DataRef, LootContext.Drop);
                short numRolledPrefixes = affixLimits?.GetMax(AffixPosition.Prefix, null) ?? 2;
                short numRolledSuffixes = affixLimits?.GetMax(AffixPosition.Suffix, null) ?? 2;
                List<AffixPrototype> allBonusAffixes = GetAllBonusAffixes();

                List<AffixPrototype> validPrefixes = new List<AffixPrototype>();
                List<AffixPrototype> validSuffixes = new List<AffixPrototype>();

                foreach (AffixPrototype affix in allBonusAffixes)
                {
                    if (affix.AllowAttachment(filterArgs))
                    {
                        if (affix.Position == AffixPosition.Prefix)
                            validPrefixes.Add(affix);
                        else if (affix.Position == AffixPosition.Suffix)
                            validSuffixes.Add(affix);
                    }
                }

                if (selectionStrategy == AffixSelectionStrategy.RandomValid)
                {
                    var randomForShuffle = new System.Random(player.Game.Random.Next());
                    int n = validPrefixes.Count;
                    while (n > 1) { n--; int k = randomForShuffle.Next(n + 1); (validPrefixes[k], validPrefixes[n]) = (validPrefixes[n], validPrefixes[k]); }

                    n = validSuffixes.Count;
                    while (n > 1) { n--; int k = randomForShuffle.Next(n + 1); (validSuffixes[k], validSuffixes[n]) = (validSuffixes[n], validSuffixes[k]); }
                }

                int actualPrefixesAdded = 0;
                for (int i = 0; i < validPrefixes.Count && actualPrefixesAdded < numRolledPrefixes; i++)
                {
                    newAffixSpecs.Add(new AffixSpec(validPrefixes[i], PrototypeId.Invalid, player.Game.Random.Next()));
                    actualPrefixesAdded++;
                }

                int actualSuffixesAdded = 0;
                for (int i = 0; i < validSuffixes.Count && actualSuffixesAdded < numRolledSuffixes; i++)
                {
                    newAffixSpecs.Add(new AffixSpec(validSuffixes[i], PrototypeId.Invalid, player.Game.Random.Next()));
                    actualSuffixesAdded++;
                }
            }

            ItemSpec finalSpec = new ItemSpec(itemProtoRef, chosenRarityProto.DataRef, maxItemLevel, 0, newAffixSpecs, player.Game.Random.Next());

            using (LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>())
            {
                lootResultSummary.Add(new LootResult(finalSpec));
                if (lootManager.GiveLootFromSummary(lootResultSummary, player))
                {
                    string strategyText = selectionStrategy.ToString();
                    Logger.Info($"CreateAndGiveItemWithExplicitPrefixSuffix: Successfully gave {GameDatabase.GetPrototypeName(itemProtoRef)} (Lvl {maxItemLevel}, {newAffixSpecs.Count} affixes, {strategyText}) to {player.GetName()}");
                    return $"Gave {GameDatabase.GetPrototypeName(itemProtoRef)} (Lvl {maxItemLevel}) with {newAffixSpecs.Count} affixes ({strategyText}). Built-ins handled by system.";
                }
                else
                {
                    Logger.Error($"CreateAndGiveItemWithExplicitPrefixSuffix: Failed to give item {GameDatabase.GetPrototypeName(itemProtoRef)} to {player.GetName()}.");
                    return "Error: Failed to give item after creating spec.";
                }
            }
        }

     
        [Command("destroyindestructible")]
        [CommandDescription("Destroys indestructible items contained in the player's general inventory.")]
        [CommandUsage("item destroyindestructible")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string DestroyIndestructible(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Inventory general = player.GetInventory(InventoryConvenienceLabel.General);

            List<Item> indestructibleItemList = new();
            foreach (var entry in general)
            {
                Item item = player.Game.EntityManager.GetEntity<Item>(entry.Id);
                if (item == null) continue;

                if (item.ItemPrototype.CanBeDestroyed == false)
                    indestructibleItemList.Add(item);
            }

            foreach (Item item in indestructibleItemList)
                item.Destroy();

            return $"Destroyed {indestructibleItemList.Count} indestructible items.";
        }

        [Command("roll")]
        [CommandDescription("Rolls the specified loot table.")]
        [CommandUsage("item roll [pattern]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string RollLootTable(string[] @params, NetClient client)
        {
            PrototypeId lootTableProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.LootTable, @params[0], client);
            if (lootTableProtoRef == PrototypeId.Invalid) return string.Empty;

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            player.Game.LootManager.TestLootTable(lootTableProtoRef, player);

            return $"Finished rolling {lootTableProtoRef.GetName()}, see the server console for results.";
        }

        [Command("rollall")]
        [CommandDescription("Rolls all loot tables.")]
        [CommandUsage("item rollall")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string RollAllLootTables(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            int numLootTables = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (PrototypeId lootTableProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<LootTablePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                player.Game.LootManager.TestLootTable(lootTableProtoRef, player);
                numLootTables++;
            }

            stopwatch.Stop();

            return $"Finished rolling {numLootTables} loot tables in {stopwatch.Elapsed.TotalMilliseconds} ms, see the server console for results.";
        }

        [Command("creditchest")]
        [CommandDescription("Converts credits to a specified number of sellable chest items. Each chest costs 500k.")]
        [CommandUsage("item creditchest [count]")] // Optional count
        [CommandInvokerType(CommandInvokerType.Client)]
        public string CreditChest(string[] @params, NetClient client)
        {
            const PrototypeId CreditItemProtoRef = (PrototypeId)13983056721138685632; // Entity/Items/Crafting/Ingredients/CreditItem500k.prototype
            const int CreditItemPrice = 500000; // Cost per chest

            int requestedChests = 1; // Default to 1 chest

            if (@params.Length > 0)
            {
                if (!int.TryParse(@params[0], out requestedChests) || requestedChests <= 0)
                {
                    return "Invalid count specified. Please provide a positive number or omit for 1 chest.";
                }
            }

            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection == null)
            {
                Logger.Error("CreditChest: PlayerConnection is null.");
                return "Error: Could not establish player connection.";
            }
            Player player = playerConnection.Player;
            if (player == null)
            {
                Logger.Error("CreditChest: Player entity is null.");
                return "Error: Could not retrieve player information.";
            }

            CurrencyPrototype creditsProto = GameDatabase.CurrencyGlobalsPrototype.CreditsPrototype;
            if (creditsProto == null)
            {
                Logger.Error("CreditChest: CreditsPrototype is null in CurrencyGlobalsPrototype. Cannot proceed.");
                return "Error: Server configuration issue with credits definition.";
            }
            PropertyId creditsProperty = new(PropertyEnum.Currency, creditsProto.DataRef);

            int chestsCreated = 0;
            long totalCreditsSpent = 0;

            for (int i = 0; i < requestedChests; i++)
            {
                PropertyValue currentCreditsPropVal = player.Properties[creditsProperty];
                long currentCredits = 0;

                // Attempt to cast/convert PropertyValue to long.
                // If PropertyValue is a default struct or doesn't hold a long, this will fail.
                try
                {
                    currentCredits = (long)currentCreditsPropVal;
                }
                catch (Exception ex) // Catch potential conversion errors (InvalidCastException or others)
                {
                    Logger.Error($"CreditChest: Could not convert credits PropertyValue to long for player {player.GetName()}. Value: '{currentCreditsPropVal}'. Error: {ex.Message}. Assuming 0 credits.");
                    currentCredits = 0;
                }

                if (currentCredits < CreditItemPrice)
                {
                    if (chestsCreated > 0)
                    {
                        return $"Created {chestsCreated} chest(s). Not enough credits for more (needed {CreditItemPrice:N0}, have {currentCredits:N0}).";
                    }
                    return $"You need at least {CreditItemPrice:N0} credits to create a chest. You have {currentCredits:N0}.";
                }

                player.Properties.AdjustProperty(-CreditItemPrice, creditsProperty);
                totalCreditsSpent += CreditItemPrice;

                player.Game.LootManager.GiveItem(CreditItemProtoRef, LootContext.CashShop, player);
                chestsCreated++;
                Logger.Trace($"CreditChest(): {player.GetName()} created chest #{i + 1}. Credits deducted: {CreditItemPrice}.");
            }

            PropertyValue finalCreditsPropVal = player.Properties[creditsProperty];
            long finalCredits = 0;
            try
            {
                finalCredits = (long)finalCreditsPropVal;
            }
            catch (Exception ex)
            {
                Logger.Error($"CreditChest: Could not convert final credits PropertyValue to long for player {player.GetName()}. Value: '{finalCreditsPropVal}'. Error: {ex.Message}. Assuming 0 credits for final display.");
                finalCredits = 0;
            }

            if (chestsCreated > 0)
            {
                return $"Successfully created {chestsCreated} Credit Chest(s). Total credits spent: {totalCreditsSpent:N0}. (Credits remaining: {finalCredits:N0})";
            }
            else
            {
                return "No credit chests were created (or an error occurred).";
            }
        }
        [Command("search")]
        [CommandDescription("Searches for an item in your stash tabs and tells you which tab it's in.")]
        [CommandUsage("item search [item name pattern]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string SearchItemInStashes(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection == null) return "Failed to get player connection.";

            Player player = playerConnection.Player;
            if (player == null) return "Failed to get player entity.";

            string searchPattern = @params[0];
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return "Please specify an item name pattern to search for.";
            }

            StringBuilder resultsBuilder = new StringBuilder();
            bool foundItemsOverall = false;

            List<PrototypeId> stashProtoRefs = new List<PrototypeId>();
            // Get all unlocked stash tabs (the 'true' for getUnlocked means only unlocked tabs)
            player.GetStashInventoryProtoRefs(stashProtoRefs, false, true);

            if (!stashProtoRefs.Any())
            {
                return "You have no unlocked stash tabs to search.";
            }

            EntityManager entityManager = player.Game.EntityManager;

            foreach (PrototypeId stashProtoRef in stashProtoRefs)
            {
                Inventory stash = player.GetInventoryByRef(stashProtoRef);
                if (stash == null) continue;

                // Get stash display name.
                // For custom names, the Player class would need a public getter for its _stashTabOptionsDict.
                // As _stashTabOptionsDict is private, we'll use the stash inventory's prototype name.
                // If you modify Player.cs to expose a method like `TryGetStashTabDisplayName(PrototypeId, out string)`,
                // you could use it here for more user-friendly tab names.
                string stashDisplayName = GameDatabase.GetPrototypeName(stash.PrototypeDataRef);

                List<string> foundItemsInThisStashList = new List<string>();

                foreach (var invEntry in stash) // Inventory.Enumerator.Entry
                {
                    Item item = entityManager.GetEntity<Item>(invEntry.Id);
                    if (item == null) continue;

                    string currentItemName = GameDatabase.GetPrototypeName(item.PrototypeDataRef);
                    // Case-insensitive partial match
                    if (currentItemName.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string suffix = item.CurrentStackSize > 1 ? $" (x{item.CurrentStackSize})" : "";
                        foundItemsInThisStashList.Add($"- {currentItemName}{suffix}");
                    }
                }

                if (foundItemsInThisStashList.Any())
                {
                    if (foundItemsOverall) // Add a newline separator if this isn't the first stash with results
                    {
                        resultsBuilder.AppendLine();
                    }
                    resultsBuilder.AppendLine($"In Stash '{stashDisplayName}':");
                    foreach (string itemNameEntry in foundItemsInThisStashList)
                    {
                        resultsBuilder.AppendLine(itemNameEntry);
                    }
                    foundItemsOverall = true;
                }
            }

            if (foundItemsOverall)
            {
                return $"Search results for '{searchPattern}':\n{resultsBuilder.ToString()}";
            }
            else
            {
                return $"No items found matching '{searchPattern}' in your stashes.";
            }
        }


        [Command("cleardeliverybox")]
        [CommandDescription("Destroys all items contained in the delivery box inventory.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ClearDeliveryBox(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            Inventory deliveryBox = player.GetInventory(InventoryConvenienceLabel.DeliveryBox);
            if (deliveryBox == null)
                return "Delivery box inventory not found.";

            int count = deliveryBox.Count;
            deliveryBox.DestroyContained();

            return $"Destroyed {count} items contained in the delivery box inventory.";
        }
    }
}