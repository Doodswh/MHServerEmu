namespace MHServerEmu.Games.GameData
{
    // Based on CalligraphySerializer::getParser

    public enum PrototypeFieldType
    {
        Invalid = -1,
        Int8 = 0,
        Int16 = 1,
        Int32 = 2,
        Int64 = 3,
        Bool = 4,
        Float32 = 8,
        Float64 = 9,
        Enum = 11,
        UnkType12 = 12,
        FunctionPtr = 13,
        PrototypeDataRef = 14,
        AssetRef = 15,
        AssetTypeRef = 16,
        CurveRef = 17,
        LocaleStringId = 27,
        PrototypeGuid = 28,
        Mixin = 30,
        Prototype = 31,
        PrototypePtr = 32,
        PrototypeRefPtr = 33,           // "Resources should not use PrototypeRefPtrs, or you should implement them"
        VectorPrototypeDataRef = 34,
        ListPrototypeDataRef = 35,
        VectorAssetDataRef = 36,
        ListAssetRef = 37,
        ListAssetTypeRef = 38,
        ListBool = 39,
        ListEnum = 40,
        ListInt8 = 41,
        ListInt16 = 42,
        ListInt32 = 43,
        ListInt64 = 44,
        ListFloat32 = 45,
        ListFloat64 = 46,
        ListString = 47,
        ListPrototypePtr = 48,          // "Lists of PrototypePtrs are not parsed as a standard prototype field"
        ListMixin = 49,                 // "Mixin lists are not parsed as a standard prototype field"
        VectorPrototypePtr = 50,        // "Vectors of PrototypePtrs are not parsed as a standard prototype field"
        VectorPrototypeRefPtr = 51,     // "Resources should not use PrototypeRefPtrs, or you should implement them"
        UnkType52 = 52,
        Vector = 53,
        PropertyId = 54,
        PropertyCollection = 55,        // "Property collections are not parsed as a standard prototype field"
        PropertyList = 56
    }
}
