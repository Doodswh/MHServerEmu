namespace MHServerEmu.Games.GameData
{
    public enum PrototypeFieldType
    {
        Invalid = -1,
        Int8,
        Int16,
        Int32,
        Int64,
        Bool,
        UInt16,
        UInt32,
        UInt64,
        Float32,
        Float64,
        Text,                   // utf-8 prefixed by int32 length
        Enum,
        SymbolicBitSet,         // byte array of flag enums (e.g. NaviContentFlags in NaviPatchEdgePrototype)
        FunctionPtr,
        PrototypeDataRef,
        AssetRef,
        AssetTypeRef,
        CurveRef,
        Vector3,
        Point3,
        IPoint3,
        Point2,
        IPoint2,
        Orientation,
        Matrix4,
        Transform3,
        Aabb,
        LocaleStringId,
        PrototypeGuid,
        ResourceName,           // same as text
        Mixin,
        Prototype,
        PrototypePtr,
        PrototypeRefPtr,        // "Resources should not use PrototypeRefPtrs, or you should implement them"
        VectorPrototypeDataRef,
        ListPrototypeDataRef,
        VectorAssetDataRef,
        ListAssetRef,
        ListAssetTypeRef,
        ListBool,
        ListEnum,
        ListInt8,
        ListInt16,
        ListInt32,
        ListInt64,
        ListFloat32,
        ListFloat64,
        ListString,
        ListPrototypePtr,       // "Lists of PrototypePtrs are not parsed as a standard prototype field"
        ListMixin,              // "Mixin lists are not parsed as a standard prototype field"
        VectorPrototypePtr,     // "Vectors of PrototypePtrs are not parsed as a standard prototype field"
        VectorPrototypeRefPtr,  // "Resources should not use PrototypeRefPtrs, or you should implement them"
        UnkType52,
        Vector,
        PropertyId,
        PropertyCollection,        // "Property collections are not parsed as a standard prototype field"
        PropertyList,
    }
}
