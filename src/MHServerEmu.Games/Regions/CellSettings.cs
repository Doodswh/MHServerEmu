using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.Regions
{
    public class CellSettings
    {
        public Vector3 PositionInArea { get; set; }
        public Orientation OrientationInArea { get; set; }
        public PrototypeId CellRef { get; set; }
        public int Seed { get; set; }
        public int BufferWidth { get; set; }
        public LocaleStringId OverrideLocationName { get; set; }
        public List<uint> ConnectedCells { get; set; }
        public PrototypeId PopulationThemeOverrideRef { get; set; }

        // CUSTOM: set when this cell stands in for another district cell (StaticAreaCellGenerator.CellSubstitutions)
        public PrototypeId StandInForRef { get; set; }              // the cell it replaces (RestrictToCells matches it too)
        public Vector3 StandInShift { get; set; }                   // how far it was moved from the original's spot
        public bool UseStandInSpawnMarkers { get; set; }            // take over the original's spawn markers
        public bool StandInSharesOriginal { get; set; }             // one of several: only the markers that fall on it
    }
}
