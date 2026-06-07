using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.GameData.Tables
{
    public class OmegaBonusSetTable
    {
        private readonly Dictionary<PrototypeId, OmegaBonusSetPrototype> _omegaBonusSets = new();

        public OmegaBonusSetTable()
        {
            AdvancementGlobalsPrototype advGlobalsProto = GameDatabase.AdvancementGlobalsPrototype;
            if (!Verify.IsNotNull(advGlobalsProto)) return;

            foreach (PrototypeId omegaBonusSetRef in advGlobalsProto.OmegaBonusSets)
            {
                OmegaBonusSetPrototype omegaBonusSetProto = omegaBonusSetRef.As<OmegaBonusSetPrototype>();

                foreach (PrototypeId omegaBonusRef in omegaBonusSetProto.OmegaBonuses)
                    _omegaBonusSets[omegaBonusRef] = omegaBonusSetProto;
            }
        }

        public OmegaBonusSetPrototype GetOmegaBonusSet(PrototypeId omegaBonusRef)
        {
            if (_omegaBonusSets.TryGetValue(omegaBonusRef, out OmegaBonusSetPrototype omegaBonusSet) == false)
                return null;

            return omegaBonusSet;
        }
    }
}
