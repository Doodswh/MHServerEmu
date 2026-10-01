using System.Collections.Concurrent;
using System.Text;
using Gazillion;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Commands.Implementations
{
    // CUSTOM: tools for checking which unused / dev zones actually load on the client, before building events on them.
    // The server can generate these zones from the game data, but the client may be missing their map files.
    [CommandGroup("zonetest")]
    [CommandGroupDescription("Tests unused / dev zones: list, go, info, mark.")]
    [CommandGroupUserLevel(AccountUserLevel.Admin)]
    public class ZoneTestCommands : CommandGroup
    {
        // Folders that hold cut / development content
        private static readonly string[] DefaultFolders = { "Regions/ZZZUNUSED/", "Regions/ZZZDevelopment/", "Regions/ZZZDemoBranch/" };

        private static readonly string ResultsFilePath = Path.Combine(FileHelper.DataDirectory, "ZoneTestResults.tsv");
        private static readonly object ResultsFileLock = new();

        // Last list shown to each player, so "go 5" works
        private static readonly ConcurrentDictionary<ulong, List<PrototypeId>> LastLists = new();

        [Command("list")]
        [CommandDescription("Lists unused / dev zones you can enter (optionally filtered by name). Numbers work with go.")]
        [CommandUsage("zonetest list [filter]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string List(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            string filter = @params.Length > 0 ? @params[0] : null;

            // With a filter, search every region; without one, only the cut / dev folders
            List<PrototypeId> regions = new();
            foreach (PrototypeId regionRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<RegionPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                string name = GameDatabase.GetPrototypeName(regionRef);
                if (string.IsNullOrEmpty(name))
                    continue;

                if (filter != null)
                {
                    if (name.Contains(filter, StringComparison.OrdinalIgnoreCase) == false)
                        continue;
                }
                else if (DefaultFolders.Any(folder => name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) == false)
                {
                    continue;
                }

                RegionPrototype regionProto = regionRef.As<RegionPrototype>();
                if (regionProto == null || regionProto.StartTarget == PrototypeId.Invalid)
                    continue;

                regions.Add(regionRef);
            }

            if (regions.Count == 0)
                return filter != null ? $"No enterable regions match '{filter}'." : "No enterable unused regions found.";

            regions.Sort((a, b) => string.Compare(GameDatabase.GetPrototypeName(a), GameDatabase.GetPrototypeName(b), StringComparison.OrdinalIgnoreCase));
            LastLists[playerConnection.PlayerDbId] = regions;

            Dictionary<PrototypeId, string> verdicts = LoadVerdicts();

            List<string> lines = new() { $"{regions.Count} zone(s). Use !zonetest go <number>:" };
            for (int i = 0; i < regions.Count; i++)
            {
                string verdict = verdicts.TryGetValue(regions[i], out string v) ? $" [{v}]" : string.Empty;
                lines.Add($"{i + 1}. {ShortName(regions[i])}{verdict}");
            }

            CommandHelper.SendMessages(client, lines);
            return string.Empty;
        }

        [Command("go")]
        [CommandDescription("Teleports you into a fresh instance of a zone, by number from the last list or by name.")]
        [CommandUsage("zonetest go [number|name]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Go(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            PrototypeId regionRef = ResolveRegion(@params[0], playerConnection, client, out string error);
            if (regionRef == PrototypeId.Invalid)
                return error;

            RegionPrototype regionProto = regionRef.As<RegionPrototype>();
            if (regionProto == null || regionProto.StartTarget == PrototypeId.Invalid)
                return $"{ShortName(regionRef)} has no entry point.";

            using Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>();
            teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Debug);

            if (teleporter.TeleportToTarget(regionProto.StartTarget) == false)
                return $"Could not enter {ShortName(regionRef)} (it may be blocked by access checks, see the server log).";

            CommandHelper.SendMessage(client, "If you get stuck loading or land in a void, the client is missing this zone's map. " +
                "Relog, then !zonetest mark <name> bad.");
            return $"Entering {GameDatabase.GetPrototypeName(regionRef)}. Use !zonetest info once you are in.";
        }

        [Command("info")]
        [CommandDescription("Shows how the current zone was generated: areas, generators and cell counts.")]
        [CommandUsage("zonetest info")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Info(string[] @params, NetClient client)
        {
            Player player = ((PlayerConnection)client).Player;
            Region region = player.GetRegion();
            if (region == null)
                return "You are not in a region.";

            List<string> lines = new()
            {
                $"Region: {GameDatabase.GetPrototypeName(region.PrototypeDataRef)}",
                $"Generator: {region.Prototype.RegionGenerator?.GetType().Name ?? "none"}, seed {region.RandomSeed}, difficulty {ShortName(region.DifficultyTierRef)}",
            };

            int areaCount = 0;
            int cellCount = 0;
            int randomAreaCount = 0;

            foreach (Area area in region.IterateAreas())
            {
                areaCount++;
                cellCount += area.Cells.Count;

                GeneratorPrototype generatorProto = area.Prototype?.Generator;
                bool isRandom = generatorProto is BaseGridAreaGeneratorPrototype || generatorProto is CanyonGridAreaGeneratorPrototype;
                if (isRandom)
                    randomAreaCount++;

                string generatorName = generatorProto?.GetType().Name.Replace("Prototype", string.Empty) ?? "none";
                lines.Add($"- {ShortName(area.PrototypeDataRef)}: {generatorName}, {area.Cells.Count} cells{(isRandom ? " (random layout)" : string.Empty)}");
            }

            lines.Add($"Total: {areaCount} area(s), {cellCount} cells, {randomAreaCount} with a random layout");

            Avatar avatar = player.CurrentAvatar;
            if (avatar != null && avatar.IsInWorld)
                lines.Add($"You: {ShortName(avatar.Area?.PrototypeDataRef ?? PrototypeId.Invalid)} / {ShortName(avatar.Cell?.PrototypeDataRef ?? PrototypeId.Invalid)} at {avatar.RegionLocation.Position}");

            CommandHelper.SendMessages(client, lines);
            return string.Empty;
        }

        [Command("mark")]
        [CommandDescription("Records a verdict for the current zone (or a named / numbered one) in Data/ZoneTestResults.tsv.")]
        [CommandUsage("zonetest mark [good|bad|partial] [note] (or: zonetest mark [number|name] [verdict] [note])")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Mark(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            PrototypeId regionRef;
            string verdict;
            int noteStart;

            if (IsVerdict(@params[0]))
            {
                // Current zone (used after "go" when the zone loaded)
                regionRef = player.GetRegion()?.PrototypeDataRef ?? PrototypeId.Invalid;
                if (regionRef == PrototypeId.Invalid)
                    return "You are not in a region.";

                verdict = @params[0].ToLowerInvariant();
                noteStart = 1;
            }
            else
            {
                // A named / numbered zone (used after relogging out of a broken zone)
                if (@params.Length < 2 || IsVerdict(@params[1]) == false)
                    return "Usage: !zonetest mark <good|bad|partial> [note], or !zonetest mark <number|name> <good|bad|partial> [note]";

                regionRef = ResolveRegion(@params[0], playerConnection, client, out string error);
                if (regionRef == PrototypeId.Invalid)
                    return error;

                verdict = @params[1].ToLowerInvariant();
                noteStart = 2;
            }

            string note = string.Join(' ', @params.Skip(noteStart)).Replace('\t', ' ');
            string line = string.Join('\t', DateTime.Now.ToString("yyyy-MM-dd HH:mm"), GameDatabase.GetPrototypeName(regionRef), verdict, player.GetName(), note);

            try
            {
                lock (ResultsFileLock)
                {
                    bool writeHeader = File.Exists(ResultsFilePath) == false;
                    using StreamWriter writer = new(ResultsFilePath, true, Encoding.UTF8);
                    if (writeHeader)
                        writer.WriteLine("Time\tRegion\tVerdict\tTester\tNote");
                    writer.WriteLine(line);
                }
            }
            catch (Exception e)
            {
                return $"Could not write {ResultsFilePath}: {e.Message}";
            }

            return $"Marked {ShortName(regionRef)} as {verdict}.";
        }

        #region Helpers

        private static bool IsVerdict(string text)
        {
            return text.Equals("good", StringComparison.OrdinalIgnoreCase)
                || text.Equals("bad", StringComparison.OrdinalIgnoreCase)
                || text.Equals("partial", StringComparison.OrdinalIgnoreCase);
        }

        private static PrototypeId ResolveRegion(string input, PlayerConnection playerConnection, NetClient client, out string error)
        {
            error = string.Empty;

            // Number from the last list
            if (int.TryParse(input, out int index))
            {
                if (LastLists.TryGetValue(playerConnection.PlayerDbId, out List<PrototypeId> list) == false)
                {
                    error = "Run !zonetest list first.";
                    return PrototypeId.Invalid;
                }

                if (index < 1 || index > list.Count)
                {
                    error = $"Pick a number from 1 to {list.Count}.";
                    return PrototypeId.Invalid;
                }

                return list[index - 1];
            }

            // Exact region name (the file name without folders) first, so "T1L1BambooRegion" doesn't clash with longer names
            foreach (PrototypeId regionRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<RegionPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                if (ShortName(regionRef).Equals(input, StringComparison.OrdinalIgnoreCase))
                    return regionRef;
            }

            // Otherwise a partial match (prints the options if there are several)
            return CommandHelper.FindPrototype(HardcodedBlueprints.Region, input, client);
        }

        private static string ShortName(PrototypeId protoRef)
        {
            if (protoRef == PrototypeId.Invalid)
                return "none";

            string name = GameDatabase.GetPrototypeName(protoRef);
            if (string.IsNullOrEmpty(name))
                return protoRef.ToString();

            name = Path.GetFileName(name);
            return name.EndsWith(".prototype", StringComparison.OrdinalIgnoreCase) ? name[..^".prototype".Length] : name;
        }

        // Latest verdict per region from the results file, shown in the list
        private static Dictionary<PrototypeId, string> LoadVerdicts()
        {
            Dictionary<PrototypeId, string> verdicts = new();

            try
            {
                string[] lines;
                lock (ResultsFileLock)
                {
                    if (File.Exists(ResultsFilePath) == false)
                        return verdicts;

                    lines = File.ReadAllLines(ResultsFilePath);
                }

                foreach (string line in lines.Skip(1))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length < 3)
                        continue;

                    PrototypeId regionRef = GameDatabase.GetPrototypeRefByName(fields[1]);
                    if (regionRef != PrototypeId.Invalid)
                        verdicts[regionRef] = fields[2];
                }
            }
            catch (Exception)
            {
                // The list still works without verdicts
            }

            return verdicts;
        }

        #endregion
    }
}
