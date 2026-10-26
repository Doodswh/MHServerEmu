// !statcheck   (admin) what the server counts for your hero's Durability bonus from gear: the hero's total
//              StatDurabilityModifier, and per equipped item each affix that gives Durability, its value, and whether it is
//              attached (counted) or held back (its level requirement is above the item's affix level). Also written to the log.

using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "statcheck") return;
    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }

    Avatar avatar = e.Player.CurrentAvatar;
    if (avatar == null) { e.Reply("No hero."); return; }

    void Say(string text)
    {
        e.Reply(text);
        Log.Info($"[statcheck] {e.Player.GetName()} {text}");
    }

    Say($"Hero total Durability bonus (StatDurabilityModifier): {(int)avatar.Properties[PropertyEnum.StatDurabilityModifier]}");

    foreach (Inventory inventory in new InventoryIterator(avatar))
    {
        if (inventory.IsEquipment == false) continue;

        foreach (var entry in inventory)
        {
            Item item = e.Player.Game.EntityManager.GetEntity<Item>(entry.Id);
            if (item == null) continue;

            int itemTotal = item.Properties[PropertyEnum.StatDurabilityModifier];
            var parts = new List<string>();
            foreach (AffixPropertiesCopyEntry affix in item.AffixPropertiesEntries)
            {
                if (affix.Properties == null || affix.Properties.HasProperty(PropertyEnum.StatDurabilityModifier) == false) continue;
                bool attached = item.Properties.HasChildCollection(affix.Properties);
                string affixName = affix.AffixProto?.ToString() ?? "?";
                affixName = affixName.Substring(affixName.LastIndexOf('/') + 1).Replace(".prototype", "");
                parts.Add($"{affixName} +{(int)affix.Properties[PropertyEnum.StatDurabilityModifier]} " +
                          (attached ? "counted" : $"HELD BACK (needs affix level {affix.LevelRequirement}, item has {(int)item.Properties[PropertyEnum.ItemAffixLevel]})"));
            }

            if (itemTotal != 0 || parts.Count > 0)
            {
                string name = item.PrototypeName.Substring(item.PrototypeName.LastIndexOf('/') + 1);
                Say($"{name}: item total +{itemTotal} | " + (parts.Count > 0 ? string.Join(" | ", parts) : "no Durability affixes"));
            }
        }
    }
});

Log.Info("Stat check loaded (!statcheck)");
