using System.Diagnostics;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Core.Collections
{
    /// <summary>
    /// Looks up <typeparamref name="T"/> representations of <see cref="Enum"/> values at runtime without boxing and using generics.
    /// </summary>
    public class SymbolicLookup<T>
    {
        // djb2 hash appears to be faster than using string keys? need to do more in-depth benchmarking here
        private readonly Dictionary<uint, T> _lookupTable;
        private readonly T _defaultValue;

        public SymbolicLookup(Type type, T defaultValue, bool ignoreUnderscorePrefix = true)
        {
            Debug.Assert(type.IsEnum);
            Debug.Assert(type.GetEnumUnderlyingType() == typeof(T));

            // Multiple names can have the same value, so we need to iterate names and parse values and not vice versa.
            string[] enumNames = Enum.GetNames(type);
            _lookupTable = new(enumNames.Length);

            foreach (string enumName in enumNames)
            {
                T enumValue = (T)Enum.Parse(type, enumName);

                // Remove the underscore prefix we add for C# compatibility
                ReadOnlySpan<char> chars = enumName;
                if (ignoreUnderscorePrefix && chars[0] == '_')
                    chars = chars[1..];

                uint hash = HashHelper.Djb2(chars);
                _lookupTable.Add(hash, enumValue);
            }

            _defaultValue = defaultValue;
        }

        public T ToLookupValue(ReadOnlySpan<char> name, out bool found)
        {
            found = false;

            if (!Verify.IsNotNull(_lookupTable)) return _defaultValue;

            uint hash = HashHelper.Djb2(name);
            found = _lookupTable.TryGetValue(hash, out T lookupValue);

            if (found == false)
                lookupValue = _defaultValue;

            return lookupValue;
        }
    }
}
