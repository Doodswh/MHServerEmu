using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    public class AssetType
    {
        // An AssetType is a collection of references to values, generally either actual assets or enums.
        // All AssetTypes and AssetValues have their own unique ids. AssetValue ids are actually string ids.

        // Enum asset types are bound to symbolic enums they represent during game database initialization:
        // DataDirectory.LoadCalligraphyDataFramework() -> PrototypeClassManager.BindAssetTypesToEnums() -> AssetDirectory.BindAssetTypes() -> AssetType.BindEnum()

        private AssetValue[] _assets;

        private Type _symEnum;                          // Type of a symbolic enum to bind to
        private Dictionary<int, int> _symbolicLookup;   // Symbolic enum value -> asset index
        private bool _enumerated;

        public AssetTypeId AssetTypeRef { get; private set; }
        public AssetTypeGuid Guid { get; private set; }
        public int MaxEnumValue { get; private set; }

        public AssetType() { }

        public override string ToString()
        {
            return GameDatabase.GetAssetTypeName(AssetTypeRef);
        }

        public bool Load(CalligraphyReader reader, AssetDirectory assetDirectory, AssetTypeId assetTypeRef, AssetTypeGuid assetTypeGuid, DataRefManager<AssetId> stringRefManager)
        {
            if (!Verify.IsNotNull(stringRefManager)) return false;
            if (!Verify.IsTrue(assetTypeRef != AssetTypeId.Invalid)) return false;

            if (!Verify.IsTrue(Guid == AssetTypeGuid.Invalid || Guid == assetTypeGuid, $"Trying to load a new asset type over and existing one {assetTypeRef.GetName()}"))
                return false;

            AssetTypeRef = assetTypeRef;
            Guid = assetTypeGuid;

            if (!Verify.IsTrue(reader.ReadHeader("TYP"))) return false;

            if (!Verify.IsTrue(reader.Read(out short numAssets), $"Unable to read num assets in {reader.SectionName}"))
                return false;

            _assets = new AssetValue[numAssets];

            const int MaxAssetLength = 1024;
            Span<byte> assetValueBuffer = stackalloc byte[MaxAssetLength];

            for (int i = 0; i < _assets.Length; i++)
            {
                if (!Verify.IsTrue(reader.Read(out AssetId assetId), $"Unable to read asset id #{i} in {reader.SectionName}"))
                    return false;

                if (!Verify.IsTrue(reader.Read(out AssetGuid assetGuid), $"Unable to read asset guid #{i} in {reader.SectionName}"))
                    return false;

                if (!Verify.IsTrue(reader.Read(out AssetValueFlags flags), $"Unable to read asset flags in {reader.SectionName}"))
                    return false;

                if (!Verify.IsTrue(reader.ReadStringUTF8(assetValueBuffer, MaxAssetLength - 1))) return false;
                string name = assetValueBuffer.GetCString();

                stringRefManager.AddDataRef(assetId, name);

                _assets[i] = new(assetId, assetGuid, flags);

                assetDirectory.AddAssetLookup(assetTypeRef, assetId, assetGuid);
            }

            return true;
        }

        /// <summary>
        /// Sets symbolic enum binding for this asset type.
        /// </summary>
        public void BindEnum(Type symbolicEnum)
        {
            if (!Verify.IsTrue(_symEnum == null || _symEnum == symbolicEnum, "Asset type has already been bound to a different symbolic enumeration"))
                return;

            _symEnum = symbolicEnum;
            Enumerate();
        }

        /// <summary>
        /// Gets an asset id from its enum value.
        /// </summary>
        public AssetId GetAssetRefFromEnum(int enumValue)
        {
            AssetValue assetValue = GetAssetValueFromEnum(enumValue);
            if (assetValue == null)
                return AssetId.Invalid;

            return assetValue.Id;
        }

        public AssetGuid GetAssetGuid(AssetId assetRef)
        {
            foreach (AssetValue assetValue in _assets)
            {
                if (assetValue.Id == assetRef)
                    return assetValue.Guid;
            }

            return AssetGuid.Invalid;
        }

        /// <summary>
        /// Finds an asset id of this type by its name.
        /// </summary>
        public AssetId FindAssetByName(string assetToFind, bool ignoreCase)
        {
            foreach (AssetValue value in _assets)
            {
                string assetName = GameDatabase.GetAssetName(value.Id);
                StringComparison flags = ignoreCase ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

                if (assetName.Equals(assetToFind, flags))
                    return value.Id;
            }

            return AssetId.Invalid;
        }
        
        /// <summary>
        /// Enumerates this asset type taking symbolic enum binding into account.
        /// </summary>
        public void Enumerate()
        {
            if (_symEnum != null)
                _symbolicLookup = new();

            AssetDirectory assetDirectory = GameDatabase.DataDirectory.AssetDirectory;

            for (int i = 0; i < _assets.Length; i++)
            {
                int enumValue;
                if (_symEnum != null)
                {
                    // Symbolic enums
                    enumValue = (int)Enum.Parse(_symEnum, GameDatabase.GetAssetName(_assets[i].Id));    // Parse value from enum type
                    MaxEnumValue = Math.Max(enumValue, MaxEnumValue);                                   // Update max value
                    _symbolicLookup.Add(enumValue, i);                                                  // Add enumValue -> AssetValue index lookup
                }
                else
                {
                    // Regular enums
                    enumValue = i;
                }

                assetDirectory.AddAssetEnumLookup(_assets[i].Id, enumValue);
            }

            // Set max enum value for assets not bound to symbolic enums
            if (_symEnum == null && _assets.Length > 0)
                MaxEnumValue = _assets.Length - 1;

            _enumerated = true;
        }

        /// <summary>
        /// Gets an <see cref="AssetValue"/> associated with the specified enum value.
        /// </summary>
        private AssetValue GetAssetValueFromEnum(int enumValue)
        {
            if (!Verify.IsTrue(_enumerated)) return null;

            if (_symEnum != null)
            {
                // Symbolic enums
                if (_symbolicLookup.TryGetValue(enumValue, out int index) == false)
                    return null;

                return _assets[index];
            }
            else
            {
                // Regular enums
                if (!Verify.IsTrue(enumValue < _assets.Length)) return null;
                return _assets[enumValue];
            }
        }

        /// <summary>
        /// A container for references to a specific asset.
        /// </summary>
        private class AssetValue(AssetId id, AssetGuid guid, AssetValueFlags flags)
        {
            public AssetId Id { get; } = id;
            public AssetGuid Guid { get; } = guid;
            public AssetValueFlags Flags { get; } = flags;
        }
    }
}
