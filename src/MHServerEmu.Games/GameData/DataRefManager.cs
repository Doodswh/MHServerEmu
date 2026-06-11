namespace MHServerEmu.Games.GameData
{
    // Note: in the client DataRef is a container class for ulong-based data ids.
    // We are currently using ulong enums as is. Every time something mentions
    // a DataRef it's actually a ulong id (e.g. PrototypeId).

    // See DataRefTypes.cs for defined id types.

    /// <summary>
    /// Manages <typeparamref name="T"/> data references. A data reference is a pair of a <see cref="string"/> name and a <see cref="ulong"/> hash of it typed as an enum.
    /// </summary>
    public class DataRefManager<T> where T: Enum
    {
        private readonly Dictionary<T, string> _references = new();
        private readonly Dictionary<string, T> _reverseLookup;
        private readonly Dictionary<T, string> _formattedNames = new();

        /// <summary>
        /// Creates a new <see cref="DataRefManager{T}"/> instance and sets up a reverse lookup dictionary if needed.
        /// </summary>
        public DataRefManager(bool useReverseLookupDict)
        {
            // We can't use a dict for reverse lookup for all ref managers because some reference
            // types (e.g. assets) can have duplicate names
            if (useReverseLookupDict)
                _reverseLookup = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Adds a new <typeparamref name="T"/> data reference.
        /// </summary>
        public void AddDataRef(T value, string name)
        {
            _references.Add(value, name);
            _reverseLookup?.Add(name, value);
        }

        /// <summary>
        /// Returns the first occurrence of a <typeparamref name="T"/> data reference with the specified name. This lookup is case insensitive.
        /// </summary>
        public T GetDataRefByName(string name)
        {
            // Try to use a lookup dict first
            if (_reverseLookup != null)
            {
                if (_reverseLookup.TryGetValue(name, out T dataRef) == false)
                    return default;

                return dataRef;
            }

            // Fall back to linear search if there's no dict
            foreach (var kvp in _references)
            {
                if (string.Equals(name, kvp.Value, StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;
            }

            return default;
        }

        /// <summary>
        /// Returns <see langword="true"/> if this <see cref="DataRefManager{T}"/> contains the specified <see cref="T"/> data reference.
        /// </summary>
        public bool ContainsDataRef(T dataRef)
        {
            return _references.ContainsKey(dataRef);
        }

        /// <summary>
        /// Returns the name of the specified <typeparamref name="T"/> data reference.
        /// </summary>
        public string GetReferenceName(T dataRef)
        {
            if (_references.TryGetValue(dataRef, out string name) == false)
                return string.Empty;

            return name;
        }

        public string GetFormattedReferenceName(T dataRef)
        {
            // Cache formatted names to avoid unnecessary string allocations.
            lock (_formattedNames)
            {
                if (_formattedNames.TryGetValue(dataRef, out string formattedName) == false)
                {
                    string name = GetReferenceName(dataRef);
                    formattedName = Path.GetFileNameWithoutExtension(name);
                    _formattedNames.Add(dataRef, formattedName);
                }

                return formattedName;
            }
        }
    }
}
