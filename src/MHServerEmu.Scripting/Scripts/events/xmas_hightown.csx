// Christmas Hightown: fills the gaps in the unused Christmas Hightown district.
//
// The 1.52 data has a full Christmas version of Hightown (Resource/Districts/MadripoorHightownDistrict_XMAS.district),
// but 8 of its 58 tiles have no map in the client, which leaves the client loading forever. This swaps those 8 for the
// normal Hightown tile at the same spot (same bounds), so the area loads with a few non-festive tiles at the edges.
//
// Turning Christmas Hightown on is a data patch, not this script: Data/Game/Patches/PatchDataXmasHightownTest.json sets
// UpperMadripoorAreaA's Generator.District to the XMAS district (server restart). This script only matters while that is on.
//
// Takes effect for Hightown instances created after it loads. Removing the script keeps the swaps until a restart.

using MHServerEmu.Games.DRAG.Generators.Areas;

const string Xmas = "Resource/Cells/DistrictCells/Madripoor_HighTown_XMAS/";
const string Normal = "Resource/Cells/DistrictCells/Madripoor_HighTown/";

var swaps = new (string Xmas, string Normal)[]
{
    ("Hightown_X0_Y6_XMAS.cell", "Hightown_X0_Y6_A_NW_A.cell"),
    ("HighTown_X4_Y7_XMAS.cell", "HighTown_X4_Y7_A_NSW_A.cell"),
    ("HighTown_X8_Y5_XMAS.cell", "HighTown_X8_Y5_Lowtown_NE_A.cell"),
    ("HighTown_X8_Y6_XMAS.cell", "HighTown_X8_Y6_Lowtown_NESWcS.cell"),
    ("HighTown_X8_Y7_XMAS.cell", "HighTown_X8_Y7_Lowtown_NW_A.cell"),
    ("HighTown_X9_Y5_XMAS.cell", "HighTown_X9_Y5_Lowtown_ES_A.cell"),
    ("HighTown_X9_Y6_XMAS.cell", "HighTown_X9_Y6_LowtownTR_ESW_A.cell"),
    ("HighTown_X9_Y7_XMAS.cell", "HighTown_X9_Y7_Lowtown_SW_A.cell"),
};

int added = 0;
foreach (var swap in swaps)
{
    if (GameDatabase.GetPrototypeRefByName(Normal + swap.Normal) == PrototypeId.Invalid)
    {
        Log.Warn($"Christmas Hightown: normal tile {swap.Normal} not found, {swap.Xmas} is left as it is");
        continue;
    }

    StaticAreaCellGenerator.CellSubstitutions[Xmas + swap.Xmas] = Normal + swap.Normal;
    added++;
}

Log.Info($"Christmas Hightown: {added} of {swaps.Length} missing tiles filled with normal Hightown tiles");
