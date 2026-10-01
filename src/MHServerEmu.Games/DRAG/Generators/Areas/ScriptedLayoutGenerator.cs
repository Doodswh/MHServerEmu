using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.DRAG.Generators.Regions;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.Scripting;

namespace MHServerEmu.Games.DRAG.Generators.Areas
{
    /// <summary>
    /// CUSTOM: Builds an area from a script-drawn map (<see cref="ScriptedLayout"/>) using the cells of one grid cell set.
    /// Each room square gets a cell whose exits match its neighbors; empty squares and the border get the set's filler cell.
    /// </summary>
    public class ScriptedLayoutGenerator : Generator
    {
        private readonly ScriptedLayout _layout;
        private readonly CellSetRegistry _registry = new();
        private float _cellSize;

        public ScriptedLayoutGenerator(ScriptedLayout layout)
        {
            _layout = layout;
        }

        public override Aabb PreGenerate(GRandom random)
        {
            Aabb bounds = Aabb.InvertedLimit;

            _registry.Initialize(true, Log);
            int loaded = _registry.LoadCellSetPath(_layout.CellSetPath);
            if (loaded == 0 || _layout.Width == 0)
            {
                Logger.Warn($"ScriptedLayoutGenerator: no cells in [Resource/Cells/{_layout.CellSetPath}] or an empty map");
                return bounds;
            }

            Aabb cellBounds = _registry.CellBounds;
            _cellSize = cellBounds.Width;

            int border = _layout.FillBorder ? 1 : 0;
            for (int row = -border; row < _layout.Height + border; row++)
            {
                for (int col = -border; col < _layout.Width + border; col++)
                    bounds += cellBounds + GetOffset(row, col);
            }

            PreGenerated = true;
            return bounds;
        }

        public override bool Generate(GRandom random, RegionGenerator regionGenerator, List<PrototypeId> areas)
        {
            if (PreGenerated == false || _cellSize <= 0f)
                return false;

            Dictionary<(int, int), Cell> rooms = new();
            List<PrototypeId> excluded = new();
            PrototypeId filler = _registry.GetAnyFiller(random);
            int border = _layout.FillBorder ? 1 : 0;

            for (int row = -border; row < _layout.Height + border; row++)
            {
                for (int col = -border; col < _layout.Width + border; col++)
                {
                    Vector3 offset = GetOffset(row, col);

                    if (_layout.IsRoom(row, col) == false)
                    {
                        if (filler != PrototypeId.Invalid)
                            Area.AddCell(AllocateCellId(), new CellSettings { CellRef = filler, PositionInArea = offset });
                        continue;
                    }

                    char c = _layout.CharAt(row, col);
                    Cell.Type type = GetRoomType(row, col);

                    // Neighbors already placed (north / west) are excluded so the same cell doesn't repeat side by side
                    excluded.Clear();
                    if (rooms.TryGetValue((row - 1, col), out Cell north)) excluded.Add(north.PrototypeDataRef);
                    if (rooms.TryGetValue((row, col - 1), out Cell west)) excluded.Add(west.PrototypeDataRef);

                    PrototypeId cellRef = PickCell(random, c, type, excluded);
                    if (cellRef == PrototypeId.Invalid)
                    {
                        Logger.Warn($"ScriptedLayoutGenerator: no cell for room '{c}' ({type}) at row {row}, column {col}");
                        continue;
                    }

                    Cell cell = Area.AddCell(AllocateCellId(), new CellSettings { CellRef = cellRef, PositionInArea = offset });
                    if (cell == null)
                        continue;

                    CellPrototype pickedProto = cellRef.As<CellPrototype>();
                    Logger.Info($"ScriptedLayout: '{c}' row {row} col {col} needs {type}, got {pickedProto?.Type} [{GameDatabase.GetPrototypeName(cellRef)}] at {Area.Origin + offset}");

                    rooms[(row, col)] = cell;

                    Vector3 center = Area.Origin + offset;
                    _layout.AddMarker(c, center);
                    if (c == ScriptedLayout.StartChar && _layout.StartPosition.HasValue == false)
                        _layout.StartPosition = center;
                }
            }

            // Connect neighboring rooms (both directions, like the static generator)
            foreach (var kvp in rooms)
            {
                (int row, int col) = kvp.Key;
                foreach ((int dr, int dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    if (rooms.TryGetValue((row + dr, col + dc), out Cell other))
                        Area.CreateCellConnection(kvp.Value, other);
                }
            }

            if (_layout.StartPosition.HasValue == false && rooms.Count > 0)
                _layout.StartPosition = rooms.Values.First().RegionBounds.Center;

            return rooms.Count > 0;
        }

        public override bool GetPossibleConnections(ConnectionList connections, in Segment segment)
        {
            // A script-built map stands alone: no connections to other areas
            connections.Clear();
            return false;
        }

        // Row 0 is the north edge (+X), columns run west to east (+Y)
        private Vector3 GetOffset(int row, int col)
        {
            return new Vector3((_layout.Height - 1 - row) * _cellSize, col * _cellSize, 0f);
        }

        private Cell.Type GetRoomType(int row, int col)
        {
            Cell.Type type = Cell.Type.None;
            if (_layout.IsRoom(row - 1, col)) type |= Cell.Type.N;
            if (_layout.IsRoom(row, col + 1)) type |= Cell.Type.E;
            if (_layout.IsRoom(row + 1, col)) type |= Cell.Type.S;
            if (_layout.IsRoom(row, col - 1)) type |= Cell.Type.W;
            return type;
        }

        private PrototypeId PickCell(GRandom random, char c, Cell.Type type, List<PrototypeId> excluded)
        {
            if (_layout.TryGetCellOverride(c, out string cellPath))
            {
                PrototypeId overrideRef = GameDatabase.GetPrototypeRefByName(cellPath);
                if (overrideRef.As<CellPrototype>() != null)
                    return overrideRef;

                Logger.Warn($"ScriptedLayoutGenerator: [{cellPath}] is not a cell, using the set instead");
            }

            // Exact exits first, then the closest type with extra exits (sets often lack dead ends / straight corridors)
            PrototypeId cellRef = _registry.GetCellSetAssetPicked(random, type, excluded);
            if (cellRef != PrototypeId.Invalid)
                return cellRef;

            foreach (Cell.Type candidate in Enumerable.Range(1, 15).Select(i => (Cell.Type)i)
                .Where(t => (t & type) == type && t != type && _registry.HasCellOfType(t))
                .OrderBy(t => System.Numerics.BitOperations.PopCount((uint)t)))
            {
                cellRef = _registry.GetCellSetAssetPicked(random, candidate, excluded);
                if (cellRef != PrototypeId.Invalid)
                    return cellRef;
            }

            return PrototypeId.Invalid;
        }
    }
}
