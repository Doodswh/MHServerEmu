using System.Collections.Concurrent;
using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.GameData.Prototypes.Markers;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.DRAG.Generators.Regions;

namespace MHServerEmu.Games.DRAG.Generators.Areas
{
    public class SingleCellAreaGenerator : Generator
    {
        /// <summary>
        /// CUSTOM: Single-cell areas built from several other cells instead, by the original cell's resource path (e.g. a map the
        /// client can't use, rebuilt from tiles it has). Each entry is a cell and where its center goes, relative to the
        /// original's center. Each takes over the original's spawn markers that fall on it. Filled by scripts.
        /// District pieces (cells built at a fixed spot in their district) can't be moved: they stay at that spot, the center is
        /// ignored, and the original's markers are mapped onto them as a group (the original's center on the pieces' center).
        /// Don't mix them with normal tiles.
        /// </summary>
        public static readonly ConcurrentDictionary<string, (string Cell, Vector3 Center)[]> CellReplacements = new(StringComparer.OrdinalIgnoreCase);

        public SingleCellAreaGenerator() { }

        // CUSTOM: the replacement cells for this area's cell, where they go in the area, and how far the original's markers
        // move to land on them (Shift: original position = stand-in position + Shift), or null
        private List<(PrototypeId CellRef, Vector3 Position, Vector3 Shift)> GetReplacement(out PrototypeId originalRef)
        {
            originalRef = GetCellPrototypeDataRef();
            if (originalRef == 0 || CellReplacements.IsEmpty) return null;
            if (CellReplacements.TryGetValue(GameDatabase.GetPrototypeName(originalRef), out var parts) == false || parts.IsNullOrEmpty()) return null;

            CellPrototype original = GameDatabase.GetPrototype<CellPrototype>(originalRef);
            if (original == null) return null;

            List<(CellPrototype Proto, Vector3 Center)> protos = new();
            foreach (var (cell, center) in parts)
            {
                CellPrototype partProto = GameDatabase.GetPrototype<CellPrototype>(GameDatabase.GetPrototypeRefByName(cell));
                if (partProto == null)
                {
                    Logger.Warn($"GetReplacement(): no cell {cell} for {original}");
                    return null;
                }
                protos.Add((partProto, center));
            }

            int districtPieces = protos.Count(p => p.Proto.IsOffsetInMapFile);
            if (districtPieces > 0 && districtPieces < protos.Count)
            {
                Logger.Warn($"GetReplacement(): {original} mixes district pieces and normal tiles; using the original");
                return null;
            }

            List<(PrototypeId, Vector3, Vector3)> placed = new();
            if (districtPieces > 0)
            {
                // at their own spot; the original's markers move onto the middle of the pieces
                Aabb bounds = Aabb.InvertedLimit;
                foreach (var (proto, _) in protos) bounds += proto.BoundingBox;
                Vector3 shift = original.BoundingBox.Center - bounds.Center;
                shift.Z = 0f;
                foreach (var (proto, _) in protos)
                    placed.Add((proto.DataRef, Vector3.Zero, shift));
                return placed;
            }

            foreach (var (proto, center) in protos)
            {
                Vector3 position = original.BoundingBox.Center + center - proto.BoundingBox.Center;
                position = new Vector3(position.X, position.Y, 0f);
                placed.Add((proto.DataRef, position, position));
            }

            return placed;
        }

        public override bool Generate(GRandom random, RegionGenerator regionGenerator, List<PrototypeId> areas)
        {
            var replacement = GetReplacement(out PrototypeId originalRef);
            if (replacement != null)
            {
                foreach (var (partRef, position, shift) in replacement)
                {
                    Area.AddCell(AllocateCellId(), new()
                    {
                        CellRef = partRef,
                        Seed = Area.RandomSeed,
                        PositionInArea = position,
                        StandInForRef = originalRef,
                        StandInShift = shift,
                        UseStandInSpawnMarkers = true,
                        StandInSharesOriginal = true
                    });
                }

                // connect neighbors, as StaticAreaCellGenerator does
                foreach (var cell in Area.CellIterator())
                {
                    if (cell == null) continue;
                    Vector3 origin = cell.RegionBounds.Center;
                    foreach (Vector3 direction in new Vector3[] { new(cell.RegionBounds.Width, 0f, 0f), new(-cell.RegionBounds.Width, 0f, 0f),
                                                                  new(0f, cell.RegionBounds.Length, 0f), new(0f, -cell.RegionBounds.Length, 0f) })
                    {
                        Vector3 position = origin + direction;
                        if (Area.IntersectsXY(position) == false) continue;
                        Cell other = Area.GetCellAtPosition(position);
                        if (other != null) Area.CreateCellConnection(cell, other);
                    }
                }

                Logger.Info($"Generate(): {GameDatabase.GetFormattedPrototypeName(originalRef)} rebuilt from {replacement.Count} cells");
                return true;
            }

            PrototypeId cellRef = GetCellPrototypeDataRef();
            if (cellRef == 0) return false;

            CellSettings cellSettings = new()
            {
                CellRef = cellRef,
                Seed = Area.RandomSeed
            };

            return Area.AddCell(AllocateCellId(), cellSettings) != null;
        }

        public override bool GetPossibleConnections(ConnectionList connections, in Segment segment)
        {
            bool connected = false;
            connections.Clear();

            CellPrototype cellProto = GetCellPrototype();
            if (cellProto == null) return false;

            Vector3 origin = Area.Origin;

            if (cellProto.MarkerSet.Markers.HasValue())
            {
                foreach (var marker in cellProto.MarkerSet.Markers)
                {
                    if (marker is not CellConnectorMarkerPrototype cellConnector) continue;

                    Vector3 connection = origin + cellConnector.Position;

                    if (segment.Start.X == segment.End.X)
                    {
                        if (Segment.EpsilonTest(connection.X, segment.Start.X, 10.0f) &&
                           connection.Y >= segment.Start.Y && connection.Y <= segment.End.Y)
                        {
                            connections.Add(connection);
                            connected = true;
                        }
                    }
                    else if (segment.Start.Y == segment.End.Y)
                    {
                        if (Segment.EpsilonTest(connection.Y, segment.Start.Y, 10.0f) &&
                            connection.X >= segment.Start.X && connection.X <= segment.End.X)
                        {
                            connections.Add(connection);
                            connected = true;
                        }
                    }
                }
            }
            return connected;
        }

        public PrototypeId GetCellPrototypeDataRef()
        {
            if (Area.Prototype.Generator is not SingleCellAreaGeneratorPrototype generatorProto) return 0;

            AssetId cellAssetRef = generatorProto.Cell;
            if (cellAssetRef == 0) return 0;

            PrototypeId cellRef = GameDatabase.GetDataRefByAsset(cellAssetRef);
            if (cellRef == 0) return 0;

            // Fix for AvengersTowerHubArea
            if (Area.PrototypeDataRef == (PrototypeId)11587794710165067927) // Regions/HUBS/AvengersTowerHUB/AvengersTowerHUBArea.prototype
                return (PrototypeId)9602664968964741817;                    // Resource/Cells/DistrictCells/Avengers_Tower/AvengersTower_HUB.cell

            return cellRef;
        }

        public CellPrototype GetCellPrototype()
        {
            PrototypeId cellRef = GetCellPrototypeDataRef();
            if (cellRef == 0) return null;

            CellPrototype proto = GameDatabase.GetPrototype<CellPrototype>(cellRef);
            if (proto == null) return null;

            return proto;
        }

        public override Aabb PreGenerate(GRandom random)
        {
            Aabb bounds = Aabb.InvertedLimit;

            var replacement = GetReplacement(out _);
            if (replacement != null)
            {
                foreach (var (partRef, position, _) in replacement)
                    bounds += GameDatabase.GetPrototype<CellPrototype>(partRef).BoundingBox.Translate(position);
                PreGenerated = true;
                return bounds;
            }

            CellPrototype cellProto = GetCellPrototype();

            if (cellProto != null)
            {
                bounds = cellProto.BoundingBox;
                PreGenerated = true;
            }

            return bounds;
        }

        public static bool CellGridBorderBehavior(Area area)
        {
            if (area == null) return false;

            GeneratorPrototype generatorProto = area.Prototype.Generator;
            var singleCellGeneratorProto = generatorProto as SingleCellAreaGeneratorPrototype;

            if (singleCellGeneratorProto != null && singleCellGeneratorProto.BorderCellSets.HasValue() && singleCellGeneratorProto.Cell != 0)
            {
                AssetId assetRef = singleCellGeneratorProto.Cell;
                PrototypeId cellRef = GameDatabase.GetDataRefByAsset(assetRef);
                CellPrototype cellP = GameDatabase.GetPrototype<CellPrototype>(cellRef);

                if (cellP == null) return false;

                CellSetRegistry registry = new();
                registry.Initialize(true, area.GenerateLog);
                foreach (var cellSetEntry in singleCellGeneratorProto.BorderCellSets)
                {
                    if (cellSetEntry == null) continue;
                    registry.LoadDirectory(cellSetEntry.CellSet, cellSetEntry, cellSetEntry.Weight, cellSetEntry.Unique);
                }

                return DoBorderBehavior(area, singleCellGeneratorProto.BorderWidth, registry, cellP.BoundingBox.Width, 1, 1);
            }

            return false;
        }

    }
}
