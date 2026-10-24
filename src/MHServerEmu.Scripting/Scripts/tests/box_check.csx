// Box check: rolls a box's loot table the same way opening the box does, without giving anything, and reports which part
// of the table fails. For boxes that "do nothing" when used.
//
//   !boxcheck                  (admin) checks the leveling artifact box
//   !boxcheck <item prototype> (admin) checks any box (full or partial prototype path)
//
// Uses your current hero's level. Results go to chat and the server log (tag [BoxCheck]). Nothing is changed.

using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Loot;

const string DefaultBox = "Entity/Items/Consumables/Prototypes/RandomGiftboxes/LevelingArtifactBoxAnniversary.prototype";
const int Rolls = 20;

string Short(PrototypeId protoRef)
{
    string name = GameDatabase.GetPrototypeName(protoRef) ?? "";
    int slash = name.LastIndexOf('/');
    return (slash >= 0 ? name.Substring(slash + 1) : name).Replace(".prototype", "");
}

// Rolls a table like ItemActionReplaceSelfLootTable does and counts successes and items
(int Ok, int Items) Roll(Player player, Avatar avatar, LootTablePrototype table, int level)
{
    int ok = 0, items = 0;
    for (int i = 0; i < Rolls; i++)
    {
        using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
        inputSettings.Initialize(LootContext.MysteryChest, player, null, level);

        using ItemResolver resolver = ObjectPoolManager.Instance.Get<ItemResolver>();
        resolver.Initialize(player.Game.Random);
        resolver.SetContext(LootContext.MysteryChest, player);

        LootRollResult result = table.RollLootTable(inputSettings.LootRollSettings, resolver);
        if (result == LootRollResult.NoRoll || result == LootRollResult.Failure)
            continue;

        using LootResultSummary summary = ObjectPoolManager.Instance.Get<LootResultSummary>();
        resolver.FillLootResultSummary(summary);
        if (summary.NumDrops > 0) ok++;
        items += summary.NumDrops;
    }
    return (ok, items);
}

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "boxcheck")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    if (avatar == null)
        return;

    var report = new List<string>();
    void Line(string text) { report.Add(text); Log.Info("[BoxCheck] " + text); }

    string wanted = e.Args.Length > 0 ? string.Join(" ", e.Args) : DefaultBox;
    PrototypeId boxRef = GameDatabase.GetPrototypeRefByName(wanted);
    if (boxRef == PrototypeId.Invalid)
    {
        foreach (PrototypeId protoRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<ItemPrototype>())
        {
            if ((GameDatabase.GetPrototypeName(protoRef) ?? "").Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                boxRef = protoRef;
                break;
            }
        }
    }

    ItemPrototype box = GameDatabase.GetPrototype<ItemPrototype>(boxRef);
    if (box == null)
    {
        e.Reply($"No item found for '{wanted}'.");
        return;
    }

    int level = avatar.CharacterLevel;
    Line($"{Short(boxRef)}: approved {box.ApprovedForUse()}, usable {box.IsUsable}, hero level {level}");

    var actions = box.ActionsTriggeredOnItemEvent?.Choices;
    if (actions == null || actions.Length == 0)
    {
        Line("The box has no on-use actions, so using it can never do anything.");
    }
    else
    {
        foreach (var action in actions)
        {
            if (action is not ItemActionReplaceSelfLootTablePrototype lootAction)
            {
                Line($"Action {action.GetType().Name}");
                continue;
            }

            LootTablePrototype table = lootAction.LootTable;
            if (table == null)
            {
                Line("Loot action with no table.");
                continue;
            }

            int rollLevel = lootAction.UseCurrentAvatarLevelForRoll ? level : 1;
            var whole = Roll(e.Player, avatar, table, rollLevel);
            Line($"Whole table {Short(table.DataRef)} at level {rollLevel}: {whole.Ok}/{Rolls} rolls gave loot ({whole.Items} items)");

            // Each branch on its own, two levels down (the box's table is table -> table -> level bands)
            void Branches(LootTablePrototype parent, string indent, int depth)
            {
                if (parent.Choices == null) return;
                int index = 0;
                foreach (var node in parent.Choices)
                {
                    index++;
                    if (node is not LootTablePrototype sub) continue;

                    string gate = "";
                    if (sub.Modifiers != null)
                        foreach (var modifier in sub.Modifiers)
                            if (modifier is LootRollRequireLevelPrototype require)
                                gate = $" (needs level {require.LevelMin}{(require.LevelMax > 0 ? "-" + require.LevelMax : "+")})";

                    var result = Roll(e.Player, avatar, sub, rollLevel);
                    Line($"{indent}branch {index}{gate}: {result.Ok}/{Rolls}");
                    if (depth < 2) Branches(sub, indent + "    ", depth + 1);
                }
            }
            Branches(table, "    ", 1);
        }
    }

    e.Reply($"Box check written to the server log ({report.Count} lines, tagged [BoxCheck]).");
    foreach (string text in report.Take(4))
        e.Reply(text);
});

Log.Info("Box check loaded (!boxcheck)");
