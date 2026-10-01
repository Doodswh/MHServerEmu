using System.Text;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Commands.Implementations
{
    // CUSTOM: shows what the SERVER actually applies from equipped gear, to tell display problems (the client computes its own
    // tooltips / character sheet) apart from real ones. Read-only, available to everyone.
    [CommandGroup("gearcheck")]
    [CommandGroupDescription("Shows the stats your equipped gear really gives on the server. Usage: !gearcheck [stat word]")]
    [CommandGroupFlags(CommandGroupFlags.SingleCommand)]
    public class GearCheckCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private const int MaxChatLines = 40;

        [DefaultCommand]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string GearCheck(string[] @params, NetClient client)
        {
            Player player = ((PlayerConnection)client).Player;
            Avatar avatar = player?.CurrentAvatar;
            if (avatar == null)
                return "No active hero.";

            string filter = @params.Length > 0 ? string.Join(' ', @params) : null;

            List<string> lines = new();
            StringBuilder log = new();
            log.AppendLine($"!gearcheck for {player.GetName()} ({avatar.PrototypeName}), filter [{filter}]");

            // The hero's own gear, then the current team-up's gear
            CheckWearer(avatar, avatar, "Hero", filter, lines, log);

            Agent teamUp = avatar.CurrentTeamUpAgent;
            if (teamUp != null)
                CheckWearer(teamUp, avatar, $"Team-up {ShortName(teamUp.PrototypeName)}", filter, lines, log);
            else
                lines.Add("(no team-up selected)");

            // The summary (stat totals) goes to the log too, so it can be copied from there
            log.AppendLine("Summary:");
            foreach (string line in lines)
                log.AppendLine("  " + line);

            Logger.Info(log.ToString());

            if (lines.Count > MaxChatLines)
            {
                int hidden = lines.Count - MaxChatLines;
                lines = lines.Take(MaxChatLines).ToList();
                lines.Add($"... {hidden} more line(s) in the server log. Narrow it down with !gearcheck <stat word>, e.g. !gearcheck fighting");
            }

            CommandHelper.SendMessages(client, lines);
            return string.Empty;
        }

        private static void CheckWearer(Agent wearer, Avatar avatar, string label, string filter, List<string> lines, StringBuilder log)
        {
            EntityManager entityManager = wearer.Game.EntityManager;
            bool isTeamUp = wearer != avatar;

            // Every affix property on this wearer's gear, by property, to find stats more than one affix touches
            Dictionary<PropertyId, List<(string Source, string Value, bool Active)>> byProperty = new();
            int itemCount = 0, affixCount = 0, heroAffixes = 0, inactive = 0;

            foreach (Inventory inventory in new InventoryIterator(wearer, InventoryIterationFlags.Equipment))
            {
                foreach (var entry in inventory)
                {
                    Item item = entityManager.GetEntity<Item>(entry.Id);
                    if (item == null)
                        continue;

                    itemCount++;
                    string itemName = ShortName(item.PrototypeName);
                    log.AppendLine($"[{label}] {itemName} ({ShortName(GameDatabase.GetPrototypeName(item.ItemSpec.RarityProtoRef))})");

                    foreach (AffixPropertiesCopyEntry affix in item.AffixPropertiesEntries)
                    {
                        if (affix.Properties == null || affix.AffixProto == null)
                            continue;

                        affixCount++;
                        bool appliesToHero = isTeamUp && item.AppliesAffixToOwnerAvatar(affix.AffixProto);
                        if (appliesToHero) heroAffixes++;

                        // Where the values end up: attached to the item (so the wearer), or on the owner hero for team-up owner affixes
                        bool active = appliesToHero
                            ? avatar.Properties.HasChildCollection(affix.Properties)
                            : item.Properties.HasChildCollection(affix.Properties);
                        if (active == false) inactive++;

                        string affixName = ShortName(affix.AffixProto.DataRef.GetName());
                        string target = isTeamUp ? (appliesToHero ? " -> hero" : " -> team-up") : string.Empty;
                        string state = active ? string.Empty : $" (NOT ACTIVE, level req {affix.LevelRequirement})";
                        log.AppendLine($"    {affixName}{target}{state}");

                        foreach (var kvp in affix.Properties)
                        {
                            if (IsNoise(kvp.Key.Enum))
                                continue;

                            string value = FormatValue(kvp.Key, kvp.Value);
                            log.AppendLine($"        {kvp.Key} = {value}");

                            if (byProperty.TryGetValue(kvp.Key, out var sources) == false)
                                byProperty[kvp.Key] = sources = new();

                            sources.Add(($"{itemName}/{affixName}{target}", value, active));
                        }
                    }
                }
            }

            lines.Add($"== {label}: {itemCount} item(s), {affixCount} affix(es)" +
                (isTeamUp ? $", {heroAffixes} apply to your hero, {affixCount - heroAffixes} to the team-up" : string.Empty) +
                (inactive > 0 ? $", {inactive} NOT active" : string.Empty));

            foreach (var kvp in byProperty.OrderBy(kvp => kvp.Key.ToString()))
            {
                string propertyName = kvp.Key.ToString();
                bool matchesFilter = filter != null && propertyName.Contains(filter, StringComparison.OrdinalIgnoreCase);

                // Without a filter only stats hit by more than one affix are listed; with one, every match
                if (filter == null ? kvp.Value.Count < 2 : matchesFilter == false)
                    continue;

                // What the server really has on the wearer (and on the hero for team-up owner affixes)
                string wearerTotal = FormatValue(kvp.Key, wearer.Properties[kvp.Key]);
                string total = isTeamUp
                    ? $"team-up total {wearerTotal}, hero total {FormatValue(kvp.Key, avatar.Properties[kvp.Key])}"
                    : $"hero total {wearerTotal}";

                string parts = string.Join(" + ", kvp.Value.Select(source => source.Active ? source.Value : $"({source.Value} inactive)"));
                lines.Add($"{propertyName}: {parts} -> {total}");
            }
        }

        private static string FormatValue(PropertyId id, PropertyValue value)
        {
            PropertyInfo info = GameDatabase.PropertyInfoTable.LookupPropertyInfo(id.Enum);
            return info.DataType switch
            {
                PropertyDataType.Real    => ((float)value).ToString("0.###"),
                PropertyDataType.Integer => ((long)value).ToString(),
                PropertyDataType.Boolean => ((bool)value).ToString(),
                _                        => value.Print(info.DataType),
            };
        }

        // Bookkeeping properties every affix carries
        private static bool IsNoise(PropertyEnum propertyEnum)
        {
            return propertyEnum == PropertyEnum.ItemLevel;
        }

        private static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "?";

            string name = Path.GetFileName(path.Replace('\\', '/'));
            return name.EndsWith(".prototype", StringComparison.OrdinalIgnoreCase) ? name[..^".prototype".Length] : name;
        }
    }
}
