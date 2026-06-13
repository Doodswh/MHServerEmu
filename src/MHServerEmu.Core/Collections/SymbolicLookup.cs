using System.Diagnostics;

namespace MHServerEmu.Core.Collections
{
    /// <summary>
    /// Looks up <typeparamref name="T"/> representations of <see cref="Enum"/> values at runtime without boxing and using generics.
    /// </summary>
    public class SymbolicLookup<T>
    {
        // Speed based on benchmark results: [djb2 key] > [linear no case] > [linear] > [string key] = [string key no case]
        // String key dictionaries are the fastest, and there is no meaningful difference when using OrdinalIgnoreCase.
        // Client-side implementation uses case insensitive linear search here.

        private readonly Dictionary<string, T> _lookupTable;
        private readonly T _defaultValue;

        public SymbolicLookup(Type type, T defaultValue, bool ignoreUnderscorePrefix = true)
        {
            Debug.Assert(type.IsEnum);
            Debug.Assert(type.GetEnumUnderlyingType() == typeof(T));

            // Multiple names can have the same value, so we need to iterate names and parse values and not vice versa.
            string[] enumNames = Enum.GetNames(type);
            _lookupTable = new(enumNames.Length, StringComparer.OrdinalIgnoreCase);

            foreach (string enumName in enumNames)
            {
                T enumValue = (T)Enum.Parse(type, enumName);

                // Remove the underscore prefix we add to enum names that start with digits for C# compatibility.
                string key = ignoreUnderscorePrefix && enumName[0] == '_' ? enumName[1..] : enumName;

                _lookupTable.Add(key, enumValue);
            }

            _defaultValue = defaultValue;
        }

        public T ToLookupValue(string name, out bool found)
        {
            //if (!Verify.IsNotNull(_lookupTable)) return _defaultValue;
            Debug.Assert(_lookupTable != null);

            found = _lookupTable.TryGetValue(name, out T lookupValue);

            if (found == false)
                lookupValue = _defaultValue;

            return lookupValue;
        }
    }
}
