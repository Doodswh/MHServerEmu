using System.Collections.Concurrent;
using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Missions.Actions
{
    public class MissionActionPlayerTeleport : MissionAction
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        /// <summary>
        /// CUSTOM: Missions whose "teleport the player" actions don't run, by part of the mission's prototype path (e.g. the
        /// Civil War Bazaar, which sent players straight back to Avengers Tower after Crossbones' ending scene: without the
        /// scene that skipped the loot and the exit). Filled by scripts.
        /// </summary>
        public static readonly ConcurrentDictionary<string, bool> SkippedFor = new(StringComparer.OrdinalIgnoreCase);

        private const float LoadingRetrySeconds = 1f;
        private const int LoadingMaxRetries = 30;

        private MissionActionPlayerTeleportPrototype _proto;
        public MissionActionPlayerTeleport(IMissionActionOwner owner, MissionActionPrototype prototype) : base(owner, prototype)
        {
            // LokiPhase1Controller
            _proto = prototype as MissionActionPlayerTeleportPrototype;
        }

        public override void Run()
        {
            if (IsSkipped())
            {
                Logger.Info($"Run(): teleport of {Mission.PrototypeName} skipped");
                return;
            }

            using var playersHandle = ListPool<Player>.Instance.Get(out List<Player> players);
            if (GetDistributors(_proto.SendTo, players))
            {
                foreach (Player player in players)
                    TeleportWhenLoaded(player, player.Game, player.Id, 0);
            }
        }

        private bool IsSkipped()
        {
            if (SkippedFor.IsEmpty) return false;
            string path = GameDatabase.GetPrototypeName(Mission.PrototypeDataRef);
            foreach (string part in SkippedFor.Keys)
                if (path.Contains(part, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // CUSTOM: a player still on a loading screen (e.g. a mission that starts as they arrive) is teleported once the
        // client has finished loading: a second region change during the first left the client stuck on the loading screen
        private void TeleportWhenLoaded(Player player, Game game, ulong playerId, int tries)
        {
            if (player.IsOnLoadingScreen && tries < LoadingMaxRetries)
            {
                Scripting.ScriptTimer.After(game, LoadingRetrySeconds, () =>
                {
                    Player current = game.EntityManager.GetEntity<Player>(playerId);
                    if (current != null) TeleportWhenLoaded(current, game, playerId, tries + 1);
                });
                return;
            }

            using Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>();
            teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Mission);

            if (_proto.TeleportRegionTarget != PrototypeId.Invalid)
                teleporter.TeleportToTarget(_proto.TeleportRegionTarget);
            else
                teleporter.TeleportToLastTown();
        }
    }
}
