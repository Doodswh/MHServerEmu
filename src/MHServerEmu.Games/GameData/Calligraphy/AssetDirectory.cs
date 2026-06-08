using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    /// <summary>
    /// Manages loaded <see cref="AssetType"/> instances.
    /// </summary>
    public sealed class AssetDirectory
    {
        private readonly Dictionary<AssetTypeId, LoadedAssetTypeRecord> _loadedAssetTypes = new();  // AssetTypeId => LoadedAssetTypeRecord
        private readonly Dictionary<AssetId, AssetTypeId> _assetIdToTypeIdLookup = new();           // AssetId => AssetTypeId
        private readonly Dictionary<AssetGuid, AssetId> _assetGuidToIdLookup = new();               // AssetGuid => AssetId

        private readonly Dictionary<AssetId, int> _assetEnumValues = new();                         // AssetId => EnumValue

        public static AssetDirectory Instance { get; } = new();

        public int AssetTypeCount { get => _loadedAssetTypes.Count; }
        public int AssetCount { get => _assetGuidToIdLookup.Count; }

        private AssetDirectory() { }

        /// <summary>
        /// Creates a new <see cref="LoadedAssetTypeRecord"/> that can hold a loaded <see cref="AssetType"/>.
        /// </summary>
        public LoadedAssetTypeRecord CreateAssetTypeRecord(AssetTypeId assetTypeRef, AssetTypeRecordFlags flags)
        {
            bool recordExists = _loadedAssetTypes.TryGetValue(assetTypeRef, out LoadedAssetTypeRecord record);
            if (!Verify.IsTrue(recordExists == false)) return record;

            record = new() { Flags = flags };
            _loadedAssetTypes.Add(assetTypeRef, record);

            return record;
        }
        
        /// <summary>
        /// Returns the <see cref="AssetType"/> with the specified <see cref="AssetTypeId"/>.
        /// </summary>
        public AssetType GetAssetType(AssetTypeId assetTypeRef)
        {
            if (_loadedAssetTypes.TryGetValue(assetTypeRef, out LoadedAssetTypeRecord record) == false)
                return null;

            return record.AssetType;
        }

        /// <summary>
        /// Returns the <see cref="AssetType"/> that the specified <see cref="AssetId"/> belongs to.
        /// </summary>
        public AssetType GetAssetType(AssetId assetRef)
        {
            if (assetRef == AssetId.Invalid)
                return null;

            AssetTypeId assetTypeRef = GetAssetTypeRef(assetRef);
            if (assetTypeRef == AssetTypeId.Invalid)
                return null;

            return GetAssetType(assetTypeRef);
        }

        /// <summary>
        /// Finds and returns an <see cref="AssetType"/> by its name.
        /// </summary>
        public AssetType GetAssetType(string name)  // Same as AssetDirectory::GetWritableAssetType()
        {
            IEnumerable<AssetTypeId> matches = GameDatabase.SearchAssetTypes(name, DataFileSearchFlags.NoMultipleMatches);   // FIXME
            if (!Verify.IsTrue(matches.Any(), $"No asset type matches for [{name}]"))
                return null;

            AssetTypeId assetTypeRef = matches.First();
            return GetAssetType(assetTypeRef);
        }

        /// <summary>
        /// Returns the <see cref="AssetTypeId"/> of the <see cref="AssetType"/> that the specified <see cref="AssetId"/> belong to.
        /// </summary>
        public AssetTypeId GetAssetTypeRef(AssetId assetRef)
        {
            if (_assetIdToTypeIdLookup.TryGetValue(assetRef, out AssetTypeId assetTypeRef) == false)
                return AssetTypeId.Invalid;

            return assetTypeRef;
        }

        public AssetId GetAssetRef(AssetGuid assetGuid)
        {
            bool found = _assetGuidToIdLookup.TryGetValue(assetGuid, out AssetId assetRef);
            
            while (found == false)
            {
                ulong guidReplacement = 0;
                
                if (DataDirectory.Instance.GetGuidReplacement((ulong)assetGuid, ref guidReplacement) && guidReplacement != 0)
                    found = _assetGuidToIdLookup.TryGetValue((AssetGuid)guidReplacement, out assetRef);
                else
                    return AssetId.Invalid;
            }

            return assetRef;
        }

        /// <summary>
        /// Returns the enum value of the specified <see cref="AssetId"/>.
        /// </summary>
        public int GetEnumValue(AssetId assetRef)
        {
            if (_assetEnumValues.TryGetValue(assetRef, out int enumValue) == false)
            {
                // Enumerate the asset type if there is no quick enum lookup for this assetId
                AssetType assetType = GetAssetType(assetRef);
                if (!Verify.IsNotNull(assetType)) return 0;
                assetType.Enumerate();

                // If there is still no lookup something must have gone wrong
                if (!Verify.IsTrue(_assetEnumValues.TryGetValue(assetRef, out enumValue) == false))
                    return 0;
            }

            return enumValue;
        }

        /// <summary>
        /// Adds new <see cref="AssetId"/> => <see cref="AssetTypeId"/> and <see cref="AssetGuid"/> => <see cref="AssetId"/> lookups.
        /// </summary>
        public void AddAssetLookup(AssetTypeId assetTypeRef, AssetId assetRef, AssetGuid assetGuid)
        {
            _assetIdToTypeIdLookup.Add(assetRef, assetTypeRef);
            _assetGuidToIdLookup.Add(assetGuid, assetRef);
        }

        /// <summary>
        /// Adds a new <see cref="AssetId"/> => enumValue lookup.
        /// </summary>
        public void AddAssetEnumLookup(AssetId assetRef, int enumValue)
        {
            _assetEnumValues.Add(assetRef, enumValue);
        }

        /// <summary>
        /// Binds <see cref="AssetType"/> instances to code enums.
        /// </summary>
        public void BindAssetTypes(Dictionary<AssetType, Type> assetEnumBindings)
        {
            foreach (LoadedAssetTypeRecord record in _loadedAssetTypes.Values)
            {
                if (assetEnumBindings.TryGetValue(record.AssetType, out Type enumBinding))
                    record.AssetType.BindEnum(enumBinding);
                else
                    record.AssetType.BindEnum(null);
            }
        }

        /// <summary>
        /// Provides an <see cref="IEnumerable{T}"/> of all loaded <see cref="AssetType"/> instances.
        /// </summary>
        public IEnumerable<AssetType> IterateAssetTypes()
        {
            foreach (var record in _loadedAssetTypes.Values)
                yield return record.AssetType;  // FIXME
        }

        /// <summary>
        /// Represents a loaded <see cref="AssetType"/> in the <see cref="AssetDirectory"/>.
        /// </summary>
        public class LoadedAssetTypeRecord
        {
            public AssetType AssetType { get; } = new();
            public AssetTypeRecordFlags Flags { get; set; }
        }
    }
}
