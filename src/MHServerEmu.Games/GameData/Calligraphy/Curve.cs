using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    /// <summary>
    /// Contains a collection of numeric values.
    /// </summary>
    public class Curve
    {
        private CurveId _curveRef;
        private float[] _values;

        public int MinPosition { get; private set; }    // m_startPosition
        public int MaxPosition { get; private set; }    // m_endPosition

        public bool IsCurveZero { get; private set; } = true;

        public Curve() { }

        public override string ToString()
        {
            return GameDatabase.GetCurveName(_curveRef);
        }

        public bool Load(CalligraphyReader reader, CurveId curveRef)
        {
            _curveRef = curveRef;

            if (!Verify.IsTrue(reader.ReadHeader("CRV"))) return false;

            if (!Verify.IsTrue(reader.Read(out int startPosition))) return false;
            MinPosition = startPosition;

            if (!Verify.IsTrue(reader.Read(out int endPosition))) return false;
            MaxPosition = endPosition;

            int numElements = endPosition - startPosition + 1;
            if (!Verify.IsTrue(numElements >= 1)) return false;

            _values = new float[numElements];
            for (int i = 0; i < numElements; i++)
            {
                if (!Verify.IsTrue(reader.Read(out double value))) return false;
                _values[i] = (float)value;
                IsCurveZero &= value == 0;
            }

            return true;
        }

        // NOTE: The client uses a bunch of copy-pasted code here for different versions of GetAt(), we just wrap the same float functions for int versions.

        /// <summary>
        /// Returns the value at the specified position as <see cref="float"/>.
        /// </summary>
        public float GetAt(int position)
        {
            if (!Verify.IsTrue(position >= MinPosition, $"Curve position ({position}) below min of ({MinPosition}) Curve: {this}"))
                return _values[0];

            if (!Verify.IsTrue(position <= MaxPosition, $"Curve position ({position}) above max of ({MaxPosition}) Curve: {this}"))
                return _values[MaxPosition - MinPosition];

            //position = Math.Clamp(position, MinPosition, MaxPosition);    // Unnecessary client-side clamp that is already handled by verify checks above
            int index = position - MinPosition;
            return _values[index];
        }

        /// <summary>
        /// Retrieves the value at the specified position as <see cref="float"/>. Returns <see langword="true"/> if successful.
        /// </summary>
        public bool GetAt(int position, out float value)
        {
            if (position < MinPosition)
            {
                value = _values[0];
                Verify.IsTrue(false, $"Curve position ({position}) below min of ({MinPosition}) Curve: {this}");
                return false;
            }
            else if (position > MaxPosition)
            {
                value = _values[MaxPosition - MinPosition];
                Verify.IsTrue(false, $"Curve position ({position}) above max of ({MaxPosition}) Curve: {this}");
                return false;
            }

            int index = position - MinPosition;
            value = _values[index];
            return true;
        }

        /// <summary>
        /// Returns the value at the specified position as <see cref="int"/>.
        /// </summary>
        public int GetIntAt(int position)
        {
            return MathHelper.RoundToInt(GetAt(position));
        }

        /// <summary>
        /// Retrieves the value at the specified position as <see cref="int"/>. Returns <see langword="true"/> if successful.
        /// </summary>
        public bool GetIntAt(int position, out int value)
        {
            bool result = GetAt(position, out float floatValue);
            value = MathHelper.RoundToInt(floatValue);
            return result;
        }

        /// <summary>
        /// Returns the value at the specified position as <see cref="long"/>.
        /// </summary>
        public long GetInt64At(int position)
        {
            return MathHelper.RoundToInt64(GetAt(position));
        }

        /// <summary>
        /// Retrieves the value at the specified position as <see cref="long"/>. Returns <see langword="true"/> if successful.
        /// </summary>
        public bool GetInt64At(int position, out long value)
        {
            bool result = GetAt(position, out float floatValue);
            value = MathHelper.RoundToInt64(floatValue);
            return result;
        }

        /// <summary>
        /// Sums the values within specified range and returns the result as <see cref="float"/>.
        /// </summary>
        public float IntegrateDiscrete(int start, int end)
        {
            if (!Verify.IsTrue(start >= MinPosition, $"Curve start (%d) below min of (%d) Curve: %s"))
                return 0f;

            if (!Verify.IsTrue(start <= MaxPosition, $"Curve start (%d) above max of (%d) Curve: %s"))
                return 0f;

            if (!Verify.IsTrue(end >= MinPosition, $"Curve end (%d) below min of (%d) Curve: %s"))
                return 0f;

            if (!Verify.IsTrue(end <= MaxPosition, $"Curve end (%d) above max of (%d) Curve: %s"))
                return 0f;

            float result = 0;
            for (int i = start; i <= end; i++)
                result += _values[i - MinPosition];
            return result;
        }

        /// <summary>
        /// Sums the values within specified range and returns the result as <see cref="int"/>.
        /// </summary>
        public int IntegrateDiscreteInt(int start, int end)
        {
            return MathHelper.RoundToInt(IntegrateDiscrete(start, end));
        }

        /// <summary>
        /// Checks if the specified index is within range of this <see cref="Curve"/>.
        /// </summary>
        public bool IndexInRange(int index)
        {
            return index >= MinPosition && index <= MaxPosition;
        }
    }
}
