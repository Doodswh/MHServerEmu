using Google.ProtocolBuffers;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Serialization;
using MHServerEmu.DatabaseAccess;
using MHServerEmu.DatabaseAccess.Json;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Locales;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Network.InstanceManagement;
using MHServerEmu.Games.Properties;
using MHServerEmu.PlayerManagement;
using MHServerEmu.PlayerManagement.Players;
using System.IO;
using System.Reflection;
using System.Text;

namespace MHServerEmu.WebFrontend.AccountDashboard
{
    public static class AccountDashboardDataService
    {
        private static long _inspectionGameId = long.MinValue;

        private delegate bool InspectionResponseBuilder(DBAccount account, Player player, out object response, out string message);

        public static bool TryBuildResponse(string email, out object response, out string message)
        {
            return TryWithRestoredPlayer(email, BuildDashboardResponse, out response, out message);
        }

        public static bool TryBuildCharacterResponse(string email, string characterId, out object response, out string message)
        {
            response = null;
            message = null;

            if (string.IsNullOrWhiteSpace(characterId))
            {
                message = "A character id is required.";
                return false;
            }

            return TryWithRestoredPlayer(email, delegate (DBAccount account, Player player, out object builtResponse, out string builtMessage)
            {
                Avatar avatar = FindAvatarById(player, characterId);
                if (avatar == null)
                {
                    builtResponse = null;
                    builtMessage = "Unable to find that saved character.";
                    return false;
                }

                List<EquipmentSlotSummary> equipment = BuildEquipmentSummary(avatar);
                List<CarriedInventorySummary> carriedInventories = BuildCarriedInventorySummary(player);
                long timePlayedSeconds = (long)Math.Max(0, Math.Floor(avatar.GetTimePlayed().TotalSeconds));

                builtResponse = new
                {
                    character = new
                    {
                        characterId = avatar.DatabaseUniqueId.ToString(),
                        name = GetAvatarName(avatar),
                        prototypeName = avatar.PrototypeName,
                        level = avatar.CharacterLevel,
                        timePlayed = timePlayedSeconds,
                    },
                    equipment = equipment.Select(slot => new
                    {
                        slot = slot.SlotKey,
                        slotLabel = slot.SlotLabel,
                        item = slot.Item == null ? null : BuildItemPayload(slot.Item),
                    }).ToList(),
                    carriedInventories = carriedInventories.Select(inventory => new
                    {
                        inventoryKey = inventory.InventoryKey,
                        inventoryLabel = inventory.InventoryLabel,
                        capacity = inventory.Capacity,
                        itemCount = inventory.Items.Count,
                        items = inventory.Items.Select(BuildItemPayload).ToList(),
                    }).ToList(),
                };
                builtMessage = null;
                return true;
            }, out response, out message);
        }

        private static bool TryWithRestoredPlayer(string email, InspectionResponseBuilder builder, out object response, out string message)
        {
            response = null;
            message = null;

            if (AccountManager.TryGetAccountByEmail(email, out DBAccount liveAccount) == false)
            {
                message = "Account not found.";
                return false;
            }

            // Block access if the player is currently online
            if (IsAccountCurrentlyOnline(liveAccount))
            {
                message = "You cannot view your dashboard while logged into the game. Please log out first.";
                return false;
            }

            // Deep Clone the Account
            // Operate entirely on a detached ghost copy so the FakeFrontendClient 
            // cannot accidentally mutate the live server's cached reference.
            DBAccount accountClone = DeepCloneAccount(liveAccount);

            if (AccountManager.LoadPlayerDataForAccount(accountClone) == false || accountClone.Player == null)
            {
                message = "Unable to load account data.";
                return false;
            }

            Game game = null;

            try
            {
                // Supplying null to GameManager isolates it, but ensure GetNextInspectionGameId() 
                // doesn't accidentally register this instance in a global save loop.
                game = new Game(GetNextInspectionGameId(), new GameManager(null));

                //  Disable Saving ---
                // If your emulator has a flag for read-only instances, set it here:
                // game.IsTransient = true; 
                // game.DisableAutoSave = true;

                using (new GameCurrentScope(game))
                {
                    Player player = BuildPlayer(game, accountClone);
                    if (player == null)
                    {
                        message = "Unable to restore player data.";
                        return false;
                    }

                    return builder(accountClone, player, out response, out message);
                }
            }
            finally
            {
                if (game != null)
                    CleanupInspectionGame(game);
            }
        }

       

        private static bool IsAccountCurrentlyOnline(DBAccount account)
        {
            PlayerHandle handle = PlayerManagerService.Instance.ClientManager.GetPlayer((ulong)account.Id);
            return handle != null && handle.IsConnected;
        }


        private static bool BuildDashboardResponse(DBAccount account, Player player, out object response, out string message)
        {
            List<CharacterSummary> characters = new();
            long totalTimePlayed = 0;
            int highestLevel = 0;

            foreach (Avatar avatar in new AvatarIterator(player, AvatarIteratorMode.IncludeArchived))
            {
                long timePlayedSeconds = (long)Math.Max(0, Math.Floor(avatar.GetTimePlayed().TotalSeconds));
                int level = avatar.CharacterLevel;

                characters.Add(new CharacterSummary
                {
                    CharacterId = avatar.DatabaseUniqueId.ToString(),
                    Name = GetAvatarName(avatar),
                    PrototypeName = avatar.PrototypeName,
                    Level = level,
                    TimePlayed = timePlayedSeconds,
                });

                totalTimePlayed += timePlayedSeconds;
                highestLevel = Math.Max(highestLevel, level);
            }

            List<CharacterSummary> orderedCharacters = characters
                .OrderByDescending(character => character.TimePlayed)
                .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var commendations = BuildCommendationsSummary(player);
            var stashes = BuildStashInventorySummary(player);
            response = new
            {
                account = new
                {
                    email = account.Email,
                    playerName = account.PlayerName,
                    accountId = account.Id.ToString(),
                    userLevel = account.UserLevel.ToString(),
                    flags = account.Flags.ToString(),
                    lastKnownMachineId = string.IsNullOrWhiteSpace(account.LastKnownMachineId) ? null : account.LastKnownMachineId,
                    lastLogout = FormatUnixMilliseconds(account.Player.LastLogoutTime),
                },
                stats = new
                {
                    totalTimePlayed,
                    totalCharacters = orderedCharacters.Count,
                    highestLevel,
                },
                commendations = commendations,
                stashes = stashes.Select(inventory => new
                {
                    inventoryKey = inventory.InventoryKey,
                    inventoryLabel = inventory.InventoryLabel,
                    capacity = inventory.Capacity,
                    itemCount = inventory.Items.Count,
                    items = inventory.Items.Select(BuildItemPayload).ToList(),
                }).ToList(),
                characters = orderedCharacters.Select(character => new
                {
                    characterId = character.CharacterId,
                    name = character.Name,
                    prototypeName = character.PrototypeName,
                    level = character.Level,
                    timePlayed = character.TimePlayed,
                }).ToList(),
            };
            message = null;
            return true;
        }

        private static Avatar FindAvatarById(Player player, string characterId)
        {
            foreach (Avatar avatar in new AvatarIterator(player, AvatarIteratorMode.IncludeArchived))
            {
                if (string.Equals(avatar.DatabaseUniqueId.ToString(), characterId, StringComparison.Ordinal))
                    return avatar;
            }

            return null;
        }
        private static List<CarriedInventorySummary> BuildStashInventorySummary(Player player)
        {
            List<CarriedInventorySummary> results = new();
            EntityManager entityManager = player.Game.EntityManager;

            // Combine both general and avatar-specific stash flags
            InventoryIterationFlags flags = InventoryIterationFlags.PlayerStashGeneral |
                                            InventoryIterationFlags.PlayerStashAvatarSpecific |
                                            InventoryIterationFlags.SortByPrototypeRef;

            foreach (Inventory inventory in new InventoryIterator(player, flags))
            {
                List<ItemSummary> items = new();

                foreach (var entry in inventory)
                {
                    Item item = entityManager.GetEntity<Item>(entry.Id);
                    if (item == null)
                        continue;

                    // Reuse your existing BuildItemSummary to get full tooltip and icon data
                    items.Add(BuildItemSummary(item, entry.Slot));
                }

                results.Add(new CarriedInventorySummary
                {
                    InventoryKey = inventory.ConvenienceLabel.ToString(),
                    InventoryLabel = GetInventoryDisplayName(inventory),
                    Capacity = inventory.GetCapacity(),
                    Items = items.OrderBy(item => item.Slot ?? int.MaxValue).ToList(),
                });
            }

            return results;
        }
        private static string BuildDynamicTooltipHtml(Item item, AccountDashboardItemBaseService.ItemBaseItem itemBaseItem)
        {
            StringBuilder html = new StringBuilder();
            ItemPrototype itemPrototype = item.PrototypeDataRef.As<ItemPrototype>();

            // 1. Name & Rarity
            string rarityColor = GetRarityHexColor(item.RarityPrototype?.Tier ?? 0);
            string itemName = string.IsNullOrWhiteSpace(itemBaseItem?.Name) ? GetItemDisplayName(item) : itemBaseItem.Name;
            html.Append($"<span class=\"tp-item-name\" style=\"color:{rarityColor}\">{itemName}</span><hr>");

            // 2. Core Stats (Item Level & Requirements)
            int itemLevel = Math.Max(0, item.Properties[PropertyEnum.ItemLevel]);
            int reqLevel = GetRequiredLevel(item);

            html.Append($"<span class=\"tp-item-left\">Item Level: {itemLevel}</span>");
            if (reqLevel > 0)
            {
                html.Append($"<span class=\"tp-item-right\">Level Required: {reqLevel}</span>");
            }
            html.Append("<hr>");

            // 3. Dynamic Affixes
            List<string> affixLines = GetDynamicAffixLines(item);
            if (affixLines.Count > 0)
            {
                foreach (string affix in affixLines)
                {
                    html.Append($"<span class=\"tp-item-info\">{affix}</span><br>");
                }
                html.Append("<hr>");
            }

            // 4. Flavor Text & Description
            string flavor = itemPrototype != null ? GetLocalizedString(itemPrototype.TooltipFlavorText) : null;
            if (!string.IsNullOrWhiteSpace(flavor))
            {
                html.Append($"<span class=\"tp-item-desc\">{flavor}</span>");
            }

            return html.ToString();
        }

        private static string GetRarityHexColor(int rarityTier)
        {
            return rarityTier switch
            {
                1 => "#FFFFFF", // Normal
                2 => "#1EFF00", // Uncommon (Green)
                3 => "#0070FF", // Rare (Blue)
                4 => "#B000FF", // Epic (Purple)
                5 => "#FF00FF", // Cosmic (Pink)
                6 => "#FFFF00", // Unique (Yellow)
                _ => "#986698"  // Default / Unknown
            };
        }

        private static string FormatDynamicPropertyValue(PropertyValue value, PropertyDataType dataType)
        {
            // Adapted from TheWatcher.Core PropertyValueFormatter
            return dataType switch
            {
                PropertyDataType.Boolean => value.RawLong != 0 ? "true" : "false",
                PropertyDataType.Integer => value.RawLong.ToString(),
                PropertyDataType.Real => value.RawFloat.ToString("F4"),
                PropertyDataType.Time => TimeSpan.FromMilliseconds(value.RawLong).ToString(),
                PropertyDataType.EntityId => $"0x{value.RawLong:X}",
                PropertyDataType.Guid => $"0x{value.RawLong:X}",
                _ => value.ToString()
            };
        }
      
        private static string GetSimplifiedItemName(string prototypePath)
        {
            if (string.IsNullOrWhiteSpace(prototypePath)) return "Unknown Item";

            // Extracted from PrototypeHelper.cs to strip paths and file extensions
            return prototypePath.Split('/').Last().Replace(".prototype", "").Replace(".defaults", "");
        }
        private static List<string> GetDynamicAffixLines(Item item)
        {
            List<string> affixLines = new();
            IReadOnlyList<AffixSpec> affixSpecs = item.ItemSpec?.AffixSpecs;

            if (affixSpecs == null || affixSpecs.Count == 0)
                return affixLines;

            foreach (AffixSpec affixSpec in affixSpecs)
            {
                string baseName = GetAffixDisplayName(affixSpec);
                if (string.IsNullOrWhiteSpace(baseName))
                    continue;

                // If the item properties dict contains the specific enum for this affix, format the live value
                // Note: You will need to map the specific PropertyEnum rolled by the AffixSpec here 
                // depending on how your specific Loot analyzer calculates the final bounds.

                if (!affixLines.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                {
                    affixLines.Add(baseName);
                }
            }

            return affixLines;
        }
        private static List<EquipmentSlotSummary> BuildEquipmentSummary(Avatar avatar)
        {
            List<EquipmentSlotSummary> results = new();
            EntityManager entityManager = avatar.Game.EntityManager;

            foreach (Inventory inventory in new InventoryIterator(avatar, InventoryIterationFlags.Equipment | InventoryIterationFlags.SortByPrototypeRef))
            {
                Item item = null;

                foreach (var entry in inventory)
                {
                    item = entityManager.GetEntity<Item>(entry.Id);
                    if (item != null)
                        break;
                }

                ItemPrototype itemPrototype = item?.PrototypeDataRef.As<ItemPrototype>();
                EquipmentInvUISlot uiSlot = itemPrototype != null ? itemPrototype.GetInventorySlotForAgent(avatar.AvatarPrototype) : EquipmentInvUISlot.Invalid;

                results.Add(new EquipmentSlotSummary
                {
                    SortOrder = uiSlot == EquipmentInvUISlot.Invalid ? int.MaxValue : (int)uiSlot,
                    SlotKey = uiSlot == EquipmentInvUISlot.Invalid ? inventory.ConvenienceLabel.ToString() : uiSlot.ToString(),
                    SlotLabel = GetEquipmentSlotLabel(uiSlot, inventory),
                    Item = item == null ? null : BuildItemSummary(item, null),
                });
            }

            return results
                .OrderBy(slot => slot.SortOrder)
                .ThenBy(slot => slot.SlotLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<CarriedInventorySummary> BuildCarriedInventorySummary(Player player)
        {
            List<CarriedInventorySummary> results = new();
            EntityManager entityManager = player.Game.EntityManager;
            InventoryIterationFlags flags = InventoryIterationFlags.PlayerGeneral | InventoryIterationFlags.PlayerGeneralExtra | InventoryIterationFlags.SortByPrototypeRef;

            foreach (Inventory inventory in new InventoryIterator(player, flags))
            {
                List<ItemSummary> items = new();

                foreach (var entry in inventory)
                {
                    Item item = entityManager.GetEntity<Item>(entry.Id);
                    if (item == null)
                        continue;

                    items.Add(BuildItemSummary(item, entry.Slot));
                }

                results.Add(new CarriedInventorySummary
                {
                    InventoryKey = inventory.ConvenienceLabel.ToString(),
                    InventoryLabel = GetInventoryDisplayName(inventory),
                    Capacity = inventory.GetCapacity(),
                    Items = items.OrderBy(item => item.Slot ?? int.MaxValue).ToList(),
                });
            }

            return results;
        }

        private static ItemSummary BuildItemSummary(Item item, uint? slot)
        {
            string prototypeName = GameDatabase.GetPrototypeName(item.PrototypeDataRef);
            ItemPrototype itemPrototype = item.PrototypeDataRef.As<ItemPrototype>();
            int itemLevel = Math.Max(0, item.Properties[PropertyEnum.ItemLevel]);
            int requiredLevel = GetRequiredLevel(item);
            List<string> affixes = GetAffixLines(item);
            AccountDashboardItemBaseService.TryGetItem(string.IsNullOrWhiteSpace(prototypeName) ? item.PrototypeName : prototypeName, out AccountDashboardItemBaseService.ItemBaseItem itemBaseItem);
            string fallbackDescription = itemPrototype != null ? GetLocalizedString(itemPrototype.TooltipDescription) : null;

            return new ItemSummary
            {
                Slot = slot.HasValue ? (int)slot.Value : null,
                SlotLabel = slot.HasValue ? $"Slot {slot.Value + 1}" : null,
                Name = string.IsNullOrWhiteSpace(itemBaseItem?.Name) == false ? itemBaseItem.Name : GetItemDisplayName(item),
                PrototypeName = string.IsNullOrWhiteSpace(prototypeName) ? item.PrototypeName : prototypeName,
                StackCount = Math.Max(1, item.CurrentStackSize),
                IconAssetName = GetItemIconAssetName(itemPrototype),
                IconUrl = itemBaseItem?.IconUrl,
                RarityName = GetRarityDisplayName(item),
                RarityTier = item.RarityPrototype?.Tier ?? 0,
                ItemLevel = itemLevel,
                RequiredLevel = requiredLevel,
                Description = string.IsNullOrWhiteSpace(itemBaseItem?.DescriptionText) == false ? itemBaseItem.DescriptionText : fallbackDescription,
                FlavorText = itemPrototype != null ? GetLocalizedString(itemPrototype.TooltipFlavorText) : null,
                Affixes = affixes,
                TooltipHtml = BuildDynamicTooltipHtml(item, itemBaseItem),
                TooltipText = itemBaseItem?.TooltipText,
                ItemBaseTypeName = itemBaseItem?.TypeName,
                ItemBaseRequiredLevelText = itemBaseItem?.RequiredLevelText,
                ItemBaseItemGradeText = itemBaseItem?.ItemGradeText,
            };
        }

        private static object BuildItemPayload(ItemSummary item)
        {
            return new
            {
                slot = item.Slot,
                slotLabel = item.SlotLabel,
                name = item.Name,
                prototypeName = item.PrototypeName,
                stackCount = item.StackCount,
                iconAssetName = item.IconAssetName,
                iconUrl = item.IconUrl,
                rarityName = item.RarityName,
                rarityTier = item.RarityTier,
                itemLevel = item.ItemLevel,
                requiredLevel = item.RequiredLevel,
                description = item.Description,
                flavorText = item.FlavorText,
                affixes = item.Affixes,
                tooltipHtml = item.TooltipHtml,
                tooltipText = item.TooltipText,
                itemBaseTypeName = item.ItemBaseTypeName,
                itemBaseRequiredLevelText = item.ItemBaseRequiredLevelText,
                itemBaseItemGradeText = item.ItemBaseItemGradeText,
            };
        }

        private static Player BuildPlayer(Game game, DBAccount account)
        {
            FakeFrontendClient frontendClient = new(account);
            PlayerConnection playerConnection = new(game, frontendClient);

            if (playerConnection.Initialize() == false)
                return null;

            return playerConnection.Player;
        }

        private static string GetAvatarName(Avatar avatar)
        {
            string localizedName = null;
            Locale locale = LocaleManager.Instance.CurrentLocale;
            if (locale != null && avatar.AvatarPrototype != null)
                localizedName = locale.GetLocaleString(avatar.AvatarPrototype.DisplayName);

            return string.IsNullOrWhiteSpace(localizedName) ? avatar.PrototypeName : localizedName;
        }

        private static string GetItemDisplayName(Item item)
        {
            ItemPrototype itemPrototype = item.PrototypeDataRef.As<ItemPrototype>();
            string localizedName = GetBestItemDisplayName(itemPrototype);
            if (string.IsNullOrWhiteSpace(localizedName) == false)
                return localizedName;

            string prototypeName = GameDatabase.GetPrototypeName(item.PrototypeDataRef);
            if (string.IsNullOrWhiteSpace(prototypeName) == false)
                return HumanizePrototypeName(prototypeName);

            return HumanizePrototypeName(item.PrototypeName);
        }

        private static string GetBestItemDisplayName(ItemPrototype itemPrototype)
        {
            if (itemPrototype == null)
                return null;

            foreach (string candidate in GetItemDisplayNameCandidates(itemPrototype))
            {
                if (string.IsNullOrWhiteSpace(candidate) == false)
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetItemDisplayNameCandidates(ItemPrototype itemPrototype)
        {
            yield return GetLocalizedString(itemPrototype.DisplayName);
            yield return GetLocalizedString(itemPrototype.DisplayNameShort);
            yield return GetLocalizedString(itemPrototype.DisplayNameInformal);

            if (itemPrototype is CostumePrototype costumePrototype)
            {
                yield return GetLocalizedString(costumePrototype.AvatarDisplayName);
                yield return GetLocalizedString(costumePrototype.AvatarDisplayNameShort);
                yield return GetLocalizedString(costumePrototype.AvatarDisplayNameInformal);
            }
        }

        private static string GetInventoryDisplayName(Inventory inventory)
        {
            string localizedName = inventory.Prototype != null ? GetLocalizedString(inventory.Prototype.DisplayName) : null;

            if (string.IsNullOrWhiteSpace(localizedName) == false)
                return localizedName;

            return HumanizeIdentifier(inventory.ConvenienceLabel.ToString());
        }

        private static string GetEquipmentSlotLabel(EquipmentInvUISlot slot, Inventory inventory)
        {
            string inventoryDisplayName = GetInventoryDisplayName(inventory);
            if (string.IsNullOrWhiteSpace(inventoryDisplayName) == false)
                return inventoryDisplayName;

            return slot switch
            {
                EquipmentInvUISlot.Gear01 => "Gear 1",
                EquipmentInvUISlot.Gear02 => "Gear 2",
                EquipmentInvUISlot.Gear03 => "Gear 3",
                EquipmentInvUISlot.Gear04 => "Gear 4",
                EquipmentInvUISlot.Gear05 => "Gear 5",
                EquipmentInvUISlot.Artifact01 => "Artifact 1",
                EquipmentInvUISlot.Artifact02 => "Artifact 2",
                EquipmentInvUISlot.Artifact03 => "Artifact 3",
                EquipmentInvUISlot.Artifact04 => "Artifact 4",
                EquipmentInvUISlot.CostumeCore => "Costume Core",
                EquipmentInvUISlot.UruForged => "Uru-Forged",
                _ => HumanizeIdentifier(slot.ToString()),
            };
        }

        private static string GetItemIconAssetName(ItemPrototype itemPrototype)
        {
            if (itemPrototype == null)
                return null;

            AssetId iconAsset = itemPrototype.StoreIconPath;
            if (itemPrototype is CostumePrototype costumePrototype && costumePrototype.StoreIconPath != AssetId.Invalid)
                iconAsset = costumePrototype.StoreIconPath;

            if (iconAsset == AssetId.Invalid)
                return null;

            string assetName = GameDatabase.GetAssetName(iconAsset);
            return string.IsNullOrWhiteSpace(assetName) ? null : assetName;
        }

        private static string GetRarityDisplayName(Item item)
        {
            RarityPrototype rarityPrototype = item.RarityPrototype;
            if (rarityPrototype == null)
                return null;

            string localizedName = GetLocalizedString(rarityPrototype.DisplayNameText);
            if (string.IsNullOrWhiteSpace(localizedName) == false)
                return localizedName;

            string prototypeName = GameDatabase.GetPrototypeName(rarityPrototype.DataRef);
            return string.IsNullOrWhiteSpace(prototypeName) ? null : HumanizePrototypeName(prototypeName);
        }

        private static List<string> GetAffixLines(Item item)
        {
            List<string> affixLines = new();
            IReadOnlyList<AffixSpec> affixSpecs = item.ItemSpec?.AffixSpecs;
            if (affixSpecs == null || affixSpecs.Count == 0)
                return affixLines;

            foreach (AffixSpec affixSpec in affixSpecs)
            {
                string affixLine = GetAffixDisplayName(affixSpec);
                if (string.IsNullOrWhiteSpace(affixLine))
                    continue;

                if (affixLines.Contains(affixLine, StringComparer.OrdinalIgnoreCase))
                    continue;

                affixLines.Add(affixLine);
            }

            return affixLines;
        }
        private static List<object> BuildCommendationsSummary(Player player)
        {
            List<object> results = new();

            foreach (CommendationChannel channel in CommendationChannels)
            {
                PrototypeId channelRef = GameDatabase.GetPrototypeRefByName(channel.PrototypeName);
                LootCooldownChannelCountPrototype channelProto = GameDatabase.GetPrototype<LootCooldownChannelCountPrototype>(channelRef);

                if (channelProto == null)
                    continue; // Skip if unavailable in the game database

                // Ensure the cooldown is updated before reading the count
                channelProto.UpdateCooldown(player, PrototypeId.Invalid);

                int count = player.Properties[PropertyEnum.LootCooldownCount, channelRef];
                int remaining = Math.Max(channelProto.MaxDrops - count, 0);

                results.Add(new
                {
                    displayName = channel.DisplayName,
                    count = count,
                    maxDrops = channelProto.MaxDrops,
                    remaining = remaining
                });
            }

            return results;
        }
        private static string GetAffixDisplayName(AffixSpec affixSpec)
        {
            AffixPrototype affixPrototype = affixSpec?.AffixProto;
            if (affixPrototype == null)
                return null;

            string localizedName = GetLocalizedString(affixPrototype.DisplayNameText);
            if (string.IsNullOrWhiteSpace(localizedName) == false)
                return localizedName;

            string prototypeName = GameDatabase.GetPrototypeName(affixPrototype.DataRef);
            return string.IsNullOrWhiteSpace(prototypeName) ? null : HumanizePrototypeName(prototypeName);
        }

        private static string GetLocalizedString(LocaleStringId stringId)
        {
            Locale locale = LocaleManager.Instance.CurrentLocale;
            if (locale == null)
                return null;

            string localizedString = locale.GetLocaleString(stringId);
            return string.IsNullOrWhiteSpace(localizedString) ? null : localizedString;
        }

        private static int GetRequiredLevel(Item item)
        {
            int requiredLevel = (int)(float)item.Properties[PropertyEnum.Requirement, PropertyEnum.CharacterLevel];
            if (requiredLevel > 0)
                return requiredLevel;

            int itemLevel = item.Properties[PropertyEnum.ItemLevel];
            if (itemLevel > 0)
                return Item.GetEquippableAtLevelForItemLevel(itemLevel);

            return 0;
        }

        private static string HumanizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            value = value.Replace('_', ' ');

            List<char> characters = new(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (i > 0 && char.IsUpper(current) && char.IsLetterOrDigit(value[i - 1]) && char.IsLower(value[i - 1]))
                    characters.Add(' ');

                characters.Add(current);
            }

            return new string(characters.ToArray()).Trim();
        }

        private static string HumanizePrototypeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            value = value.Replace('\\', '/').Trim();

            int slashIndex = value.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < value.Length - 1)
                value = value[(slashIndex + 1)..];

            int dotIndex = value.IndexOf('.');
            if (dotIndex > 0)
                value = value[..dotIndex];

            value = value
                .Replace("Item_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Item", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Prototype", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Proto", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim('_', '-', ' ');

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            return HumanizeIdentifier(value);
        }

        private static string FormatUnixMilliseconds(long value)
        {
            if (value <= 0)
                return null;

            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime.ToString("O");
        }

        private static ulong GetNextInspectionGameId()
        {
            return unchecked((ulong)Interlocked.Increment(ref _inspectionGameId));
        }

        private static void CleanupInspectionGame(Game game)
        {
            try
            {
                game.EntityManager.ProcessDeferredLists();
            }
            catch
            {
            }
        }
        private readonly record struct CommendationChannel(string DisplayName, string PrototypeName);

        private static readonly CommendationChannel[] CommendationChannels =
        [
            new("Hero Commendations", "Loot/Cooldowns/Channels/EyeOfDemonfireChannelCount.prototype"),
            new("Protector Commendations", "Loot/Cooldowns/Channels/HeartOfDemonfireChannelCount.prototype")
        ];
        private sealed class CharacterSummary
        {
            public string CharacterId { get; init; }
            public string Name { get; init; }
            public string PrototypeName { get; init; }
            public int Level { get; init; }
            public long TimePlayed { get; init; }
        }

        private sealed class EquipmentSlotSummary
        {
            public int SortOrder { get; init; }
            public string SlotKey { get; init; }
            public string SlotLabel { get; init; }
            public ItemSummary Item { get; init; }
        }

        private sealed class CarriedInventorySummary
        {
            public string InventoryKey { get; init; }
            public string InventoryLabel { get; init; }
            public int Capacity { get; init; }
            public List<ItemSummary> Items { get; init; }
        }

        private sealed class ItemSummary
        {
            public int? Slot { get; init; }
            public string SlotLabel { get; init; }
            public string Name { get; init; }
            public string PrototypeName { get; init; }
            public int StackCount { get; init; }
            public string IconAssetName { get; init; }
            public string IconUrl { get; init; }
            public string RarityName { get; init; }
            public int RarityTier { get; init; }
            public int ItemLevel { get; init; }
            public int RequiredLevel { get; init; }
            public string Description { get; init; }
            public string FlavorText { get; init; }
            public List<string> Affixes { get; init; }
            public string TooltipHtml { get; init; }
            public string TooltipText { get; init; }
            public string ItemBaseTypeName { get; init; }
            public string ItemBaseRequiredLevelText { get; init; }
            public string ItemBaseItemGradeText { get; init; }
        }

        private sealed class GameCurrentScope : IDisposable
        {
            private static readonly FieldInfo CurrentField = typeof(Game).GetField("Current", BindingFlags.Static | BindingFlags.NonPublic);

            private readonly object _previousValue;

            public GameCurrentScope(Game game)
            {
                _previousValue = CurrentField?.GetValue(null);
                CurrentField?.SetValue(null, game);
            }

            public void Dispose()
            {
                CurrentField?.SetValue(null, _previousValue);
            }
        }
        private static DBAccount DeepCloneAccount(DBAccount original)
        {
            if (original == null)
                return null;

            using MemoryStream memoryStream = new MemoryStream();

            // 1. Serialize the live account reference directly into memory
            DBAccountBinarySerializer.Serialize(memoryStream, original);

            // 2. Rewind the stream's position back to the start
            memoryStream.Position = 0;

            // 3. Deserialize it into a brand new, completely detached object
            return DBAccountBinarySerializer.Deserialize(memoryStream);
        }
        private sealed class FakeFrontendClient : IFrontendClient, IDBAccountOwner
        {
            public bool IsConnected { get; private set; } = true;
            public IFrontendSession Session { get; }
            public ulong DbId { get; }
            public DBAccount Account { get; }

            public FakeFrontendClient(DBAccount account)
            {
                Account = account;
                DbId = (ulong)account.Id;
                Session = new FakeFrontendSession(account);
            }

            public void Disconnect()
            {
                IsConnected = false;
            }

            public void SuspendReceiveTimeout() { }
            public bool AssignSession(IFrontendSession session) => true;
            public bool HandleIncomingMessageBuffer(ushort muxId, in MessageBuffer messageBuffer) => true;
            public void SendMuxCommand(ushort muxId, MuxCommand command) { }
            public void SendMessage(ushort muxId, IMessage message) { }
            public void SendMessageList(ushort muxId, List<IMessage> messageList) { }
        }

        private sealed class FakeFrontendSession : IFrontendSession
        {
            public ulong Id { get; }
            public object Account { get; }
            public string Locale { get; } = "en_us";

            public FakeFrontendSession(DBAccount account)
            {
                Id = (ulong)account.Id;
                Account = account;
            }
        }
    }
}


