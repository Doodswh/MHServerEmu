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
    public class StaticAreaCellGenerator : Generator
    {
        /// <summary>
        /// CUSTOM: District cells to build as another cell, by resource path (e.g. a seasonal district's tiles that have no map
        /// in the client, swapped for the normal tile at the same spot). An empty value leaves the cell out of the district.
        /// Filled by scripts; read when an area is generated.
        /// </summary>
        public static readonly ConcurrentDictionary<string, string> CellSubstitutions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// CUSTOM: Extra move (XY) for a substituted cell, by the original cell's resource path. A substitute is first placed so its
        /// center sits on the original's center; this nudges it further (e.g. a smaller tile standing in for a big arena, pushed
        /// against its neighbor). Filled by scripts.
        /// </summary>
        public static readonly ConcurrentDictionary<string, Vector3> CellSubstitutionOffsets = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// CUSTOM: Substituted cells (by the original's resource path) that take over the original's spawn markers, each moved
        /// to the nearest spawn spot of the stand-in, so missions and scenarios that place bosses on the original's markers
        /// still find them. Filled by scripts.
        /// </summary>
        public static readonly ConcurrentDictionary<string, bool> CellSubstitutionSpawnMarkers = new(StringComparer.OrdinalIgnoreCase);

        private static PrototypeId GetCellRef(string resource)
        {
            if (CellSubstitutions.TryGetValue(resource, out string substitute))
            {
                if (string.IsNullOrEmpty(substitute))
                    return PrototypeId.Invalid;   // left out: every caller skips cells without a ref

                resource = substitute;
            }

            return GameDatabase.GetPrototypeRefByName(resource);
        }

        /// <summary>
        /// The cell to build for a district marker and where: a substitute is moved so it covers the original's spot (cells in
        /// a district carry their position in their own bounds, and a tile from another set has different bounds).
        /// </summary>
        private static PrototypeId GetCellPlacement(ResourceMarkerPrototype marker, out Vector3 position)
        {
            position = marker.Position;
            PrototypeId cellRef = GetCellRef(marker.Resource);
            if (cellRef == PrototypeId.Invalid || CellSubstitutions.ContainsKey(marker.Resource) == false)
                return cellRef;

            CellPrototype original = GameDatabase.GetPrototype<CellPrototype>(GameDatabase.GetPrototypeRefByName(marker.Resource));
            CellPrototype substitute = GameDatabase.GetPrototype<CellPrototype>(cellRef);
            if (original == null || substitute == null)
                return cellRef;

            Vector3 shift = original.BoundingBox.Center - substitute.BoundingBox.Center;
            shift.Z = 0f;
            if (CellSubstitutionOffsets.TryGetValue(marker.Resource, out Vector3 extra))
                shift += new Vector3(extra.X, extra.Y, 0f);

            position += shift;
            return cellRef;
        }

        public StaticAreaCellGenerator() { }

        public override Aabb PreGenerate(GRandom random)
        {
            Aabb bounds = Aabb.InvertedLimit;
            DistrictPrototype protoDistrict = GetDistrictPrototype();
            if (protoDistrict == null || protoDistrict.CellMarkerSet.Markers.IsNullOrEmpty()) return bounds;

            foreach (var cellMarker in protoDistrict.CellMarkerSet.Markers)
            {
                if (cellMarker is not ResourceMarkerPrototype resourceMarker) continue;
                PrototypeId cellRef = GetCellPlacement(resourceMarker, out Vector3 cellPosition);
                if (cellRef == 0)
                {
                    if (Log) Logger.Warn($"Unable to link Resource {resourceMarker.Resource} to a corresponding .cell file");
                    continue;
                }

                CellPrototype cellProto = GameDatabase.GetPrototype<CellPrototype>(cellRef);
                if (cellProto == null) continue;
                // CUSTOM: move the cell's box to its position (Aabb + Vector3 only grows the box to take in the point, which
                // gave the same result while every marker sat at the origin, but not for a moved substitute)
                bounds += cellProto.BoundingBox.Translate(cellPosition);

            }

            PreGenerated = true;
            return bounds;
        }

        public override bool Generate(GRandom random, RegionGenerator regionGenerator, List<PrototypeId> areas)
        {

            DistrictPrototype protoDistrict = GetDistrictPrototype();
            if (protoDistrict == null) return false;

            foreach (var cellMarker in protoDistrict.CellMarkerSet.Markers)
            {
                if (cellMarker is not ResourceMarkerPrototype resourceMarker) continue;

                PrototypeId cellRef = GetCellPlacement(resourceMarker, out Vector3 cellPosition); // GetDataRefByResourceGuid
                if (cellRef == 0) continue;

                CellSettings cellSettings = new()
                {
                    PositionInArea = cellPosition,
                    OrientationInArea = resourceMarker.Rotation,
                    CellRef = cellRef
                };

                if (CellSubstitutions.ContainsKey(resourceMarker.Resource))
                {
                    cellSettings.StandInForRef = GameDatabase.GetPrototypeRefByName(resourceMarker.Resource);
                    cellSettings.StandInShift = cellPosition - resourceMarker.Position;
                    cellSettings.UseStandInSpawnMarkers = CellSubstitutionSpawnMarkers.ContainsKey(resourceMarker.Resource);
                }

                Area.AddCell(AllocateCellId(), cellSettings);
            }

            Area area = Area;
            foreach (var cell in area.CellIterator())
            {
                if (cell != null)
                {
                    Vector3 origin = cell.RegionBounds.Center;
                    float x = cell.RegionBounds.Width;
                    float y = cell.RegionBounds.Length;

                    void TryCreateConnection(Vector3 direction)
                    {
                        Vector3 position = origin + direction;
                        if (area.IntersectsXY(position))
                        {
                            Cell otherCell = area.GetCellAtPosition(position);
                            if (otherCell != null) area.CreateCellConnection(cell, otherCell);
                        }
                    }

                    TryCreateConnection(new(x, 0.0f, 0.0f));
                    TryCreateConnection(new(-x, 0.0f, 0.0f));
                    TryCreateConnection(new(0.0f, y, 0.0f));
                    TryCreateConnection(new(0.0f, -y, 0.0f));
                }
            }

            return true;
        }

        public DistrictPrototype GetDistrictPrototype()
        {
            Area area = Area;
            if (area == null)
            {
                if (Log) Logger.Warn("Unable to get SArea");
                return null;
            }

            if (Area.Prototype.Generator is not DistrictAreaGeneratorPrototype proto) return null;

            AssetId districtAssetRef = proto.District;
            if (districtAssetRef == 0)
            {
                if (Log) Logger.Warn("StaticAreaCellGenerator called with no layout specified.");
                return null;
            }

            PrototypeId districtRef = GameDatabase.GetDataRefByAsset(districtAssetRef);
            DistrictPrototype protoDistrict = GameDatabase.GetPrototype<DistrictPrototype>(districtRef);
            if (protoDistrict == null)
                if (Log) Logger.Warn($"District Prototype is not available. Likely a missing file. Looking for Asset: {GameDatabase.GetAssetName(districtAssetRef)}");

            area.DistrictDataRef = districtRef;
            return protoDistrict;
        }

        public override bool GetPossibleConnections(ConnectionList connections, in Segment segment)
        {
            connections.Clear();

            bool connected = false;
            Vector3 origin = Area.Origin;

            DistrictPrototype protoDistrict = GetDistrictPrototype();
            if (protoDistrict == null)
            {
                if (Log) Logger.Warn($"StaticArea's District is Invalid");
                return false;
            }

            if (protoDistrict.CellMarkerSet.Markers.IsNullOrEmpty())
            {
                if (Log) Logger.Warn($"StaticArea's District contains no cells");
                return false;
            }

            foreach (var cellMarker in protoDistrict.CellMarkerSet.Markers)
            {
                if (cellMarker is not ResourceMarkerPrototype resourceMarker) continue;

                PrototypeId cellRef = GetCellPlacement(resourceMarker, out Vector3 offset);
                if (cellRef == 0) continue;

                CellPrototype cellProto = GameDatabase.GetPrototype<CellPrototype>(cellRef);
                if (cellProto == null) continue;

                if (cellProto.MarkerSet.Markers.HasValue())
                {
                    foreach (var marker in cellProto.MarkerSet.Markers)
                    {
                        if (marker is not CellConnectorMarkerPrototype cellConnector) continue;

                        Vector3 connection = origin + offset + cellConnector.Position;

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
            }
            return connected;
        }

    }
}
