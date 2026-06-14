using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    public sealed class CurveDirectory
    {
        private readonly Dictionary<CurveId, CurveRecord> _curves = new();

        public static CurveDirectory Instance { get; } = new();

        public int RecordCount { get => _curves.Count; }

        private CurveDirectory() { }

        public CurveRecord CreateCurveRecord(CurveId curveRef, CurveRecordFlags flags)
        {
            if (!Verify.IsTrue(curveRef != CurveId.Invalid)) return null;

            bool recordExists = _curves.TryGetValue(curveRef, out CurveRecord record);
            if (!Verify.IsTrue(recordExists == false, "Curve record already exists, returning existing record"))
                return record;

            record = new() { Flags = flags };
            _curves.Add(curveRef, record);

            return record;
        }

        public CurveRecord GetCurveRecord(CurveId curveRef)
        {
            if (_curves.TryGetValue(curveRef, out CurveRecord record) == false)
                return null;

            return record;
        }

        public Curve GetCurve(CurveId curveRef)
        {
            if (!Verify.IsTrue(curveRef != CurveId.Invalid)) return null;
            if (!Verify.IsTrue(_curves.TryGetValue(curveRef, out CurveRecord record))) return null;

            // Load the curve if needed
            if (record.Curve == null)
            {
                string curveFilename = $"Calligraphy/{GameDatabase.GetCurveName(curveRef)}";
                using Stream fileStream = PakFileSystem.Instance.LoadFromPak(curveFilename, (int)PakFileId.Calligraphy);
                if (!Verify.IsNotNull(fileStream, $"Unable to open file %s"))
                    return null;

                using CalligraphyReader curveReader = new(fileStream, curveFilename);

                record.Curve = new();
                Verify.IsTrue(record.Curve.Load(curveReader, curveRef));
            }
            
            return record.Curve;
        }

        public class CurveRecord
        {
            public Curve Curve { get; set; }
            public CurveRecordFlags Flags { get; set; }
        }
    }
}
