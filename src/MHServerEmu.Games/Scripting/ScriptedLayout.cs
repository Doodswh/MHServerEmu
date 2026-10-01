using MHServerEmu.Core.VectorMath;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// A hand-built map for one area, drawn as text rows (see <see cref="RegionGeneratingArgs.BuildLayout"/>).
    /// </summary>
    /// <remarks>
    /// Every character is one grid square (one cell of the cell set). The first row is the north edge, columns run west to east.
    /// <list type="bullet">
    /// <item><c>#</c> or space: no room (filled with the cell set's filler cell, so it looks solid).</item>
    /// <item>Anything else: a room. Rooms next to each other are connected; the generator picks a cell of the set with exactly
    /// those exits (or the closest one with extra exits if the set has none).</item>
    /// <item><c>S</c>: a room where players arrive.</item>
    /// <item>Any other letter / digit: a room the script can find afterwards with <see cref="ScriptLayout.GetMarkers"/>
    /// (objective spots, boss arenas...). <see cref="SetCell"/> forces a specific cell for a character.</item>
    /// </list>
    /// </remarks>
    public sealed class ScriptedLayout
    {
        public const char StartChar = 'S';

        private readonly Dictionary<char, string> _cellOverrides = new();
        private readonly Dictionary<char, List<Vector3>> _markers = new();

        /// <summary>Cell set folder under Resource/Cells, e.g. "EndGame/Limbo/Limbo_A".</summary>
        public string CellSetPath { get; }
        public string[] Rows { get; }
        public string AreaNameContains { get; }

        /// <summary>Surround the map with a ring of filler cells so its edges look solid.</summary>
        public bool FillBorder { get; set; } = true;

        public int Height { get => Rows.Length; }
        public int Width { get; }

        /// <summary>Set once the map has been built: arrival point (center of the first <c>S</c> room).</summary>
        public Vector3? StartPosition { get; internal set; }
        internal bool Used { get; set; }

        internal ScriptedLayout(string cellSetPath, string[] rows, string areaNameContains)
        {
            CellSetPath = (cellSetPath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (CellSetPath.StartsWith("Resource/Cells/", StringComparison.OrdinalIgnoreCase))
                CellSetPath = CellSetPath["Resource/Cells/".Length..];

            Rows = rows ?? Array.Empty<string>();
            AreaNameContains = areaNameContains ?? string.Empty;
            Width = Rows.Length == 0 ? 0 : Rows.Max(row => row.Length);
        }

        /// <summary>
        /// Uses a specific cell (full path, e.g. "Resource/Cells/EndGame/Limbo/Limbo_C/Limbo_NESW_C.cell") for every room drawn
        /// with <paramref name="c"/>. The cell must be the same size as the set's cells and should have exits matching its neighbors.
        /// </summary>
        public ScriptedLayout SetCell(char c, string cellPath)
        {
            _cellOverrides[c] = cellPath;
            return this;
        }

        public bool IsRoom(int row, int col)
        {
            if (row < 0 || row >= Rows.Length || col < 0)
                return false;

            string line = Rows[row];
            if (col >= line.Length)
                return false;

            char c = line[col];
            return c != '#' && c != ' ';
        }

        public char CharAt(int row, int col) => IsRoom(row, col) ? Rows[row][col] : '#';

        internal bool TryGetCellOverride(char c, out string cellPath) => _cellOverrides.TryGetValue(c, out cellPath);

        internal void AddMarker(char c, Vector3 position)
        {
            if (_markers.TryGetValue(c, out List<Vector3> list) == false)
                _markers[c] = list = new();

            list.Add(position);
        }

        /// <summary>Region positions (room centers) of every room drawn with <paramref name="c"/>, in reading order.</summary>
        public IReadOnlyList<Vector3> GetMarkers(char c)
        {
            return _markers.TryGetValue(c, out List<Vector3> list) ? list : Array.Empty<Vector3>();
        }
    }
}
