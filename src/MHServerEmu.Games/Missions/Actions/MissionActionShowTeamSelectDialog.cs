using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.Missions.Actions
{
    public class MissionActionShowTeamSelectDialog : MissionAction
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private MissionActionShowTeamSelectDialogPrototype _proto;

        public MissionActionShowTeamSelectDialog(IMissionActionOwner owner, MissionActionPrototype prototype) : base(owner, prototype)
        {
            // NotInGame CivilWarEventProgressionMissionPhase02
            _proto = prototype as MissionActionShowTeamSelectDialogPrototype;
        }

        // CUSTOM: was empty. Opens the public event's choose-a-side panel for the mission's players who have no team yet
        // (their pick comes back as NetMessageSelectPublicEventTeam, see PlayerConnection.OnSelectPublicEventTeam).
        public override void Run()
        {
            var eventProto = GameDatabase.GetPrototype<PublicEventPrototype>(_proto.PublicEvent);
            if (eventProto == null) return;

            using var playersHandle = ListPool<Player>.Instance.Get(out List<Player> players);
            if (GetDistributors(DistributionType.Participants, players) == false) return;

            foreach (Player player in players)
            {
                if (player.GetPublicEventTeam(eventProto) != PrototypeId.Invalid) continue;
                player.SendMessage(NetMessageTeamSelectDialog.CreateBuilder().SetPublicEventProtoId((ulong)eventProto.DataRef).Build());
            }

            if (MissionManager.Debug) Logger.Debug($"ShowTeamSelectDialog {Mission.PrototypeName} {eventProto}");
        }
    }
}
