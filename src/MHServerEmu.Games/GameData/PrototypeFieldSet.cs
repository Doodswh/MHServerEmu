using System.Diagnostics;
using System.Reflection;
using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData
{
    public class PrototypeFieldSet
    {
        private static readonly Dictionary<Type, PrototypeFieldType> TypeToPrototypeFieldTypeEnumLookup = new()
        {
            { typeof(bool),                         PrototypeFieldType.Bool },
            { typeof(sbyte),                        PrototypeFieldType.Int8 },
            { typeof(short),                        PrototypeFieldType.Int16 },
            { typeof(int),                          PrototypeFieldType.Int32 },
            { typeof(long),                         PrototypeFieldType.Int64 },
            { typeof(float),                        PrototypeFieldType.Float32 },
            { typeof(double),                       PrototypeFieldType.Float64 },
            { typeof(string),                       PrototypeFieldType.Text },
            { typeof(Enum),                         PrototypeFieldType.Enum },
            { typeof(PrototypeId),                  PrototypeFieldType.PrototypeDataRef },
            { typeof(AssetId),                      PrototypeFieldType.AssetRef },
            { typeof(AssetTypeId),                  PrototypeFieldType.AssetTypeRef },
            { typeof(CurveId),                      PrototypeFieldType.CurveRef },
            { typeof(Vector3),                      PrototypeFieldType.Vector3 },
            { typeof(Vector2),                      PrototypeFieldType.Point2 },
            { typeof(Orientation),                  PrototypeFieldType.Orientation },
            { typeof(Aabb),                         PrototypeFieldType.Aabb },
            { typeof(LocaleStringId),               PrototypeFieldType.LocaleStringId },
            { typeof(PrototypeGuid),                PrototypeFieldType.PrototypeGuid },
            { typeof(Prototype),                    PrototypeFieldType.PrototypePtr },
            { typeof(PropertyId),                   PrototypeFieldType.PropertyId },
            { typeof(bool[]),                       PrototypeFieldType.ListBool },
            { typeof(sbyte[]),                      PrototypeFieldType.ListInt8 },
            { typeof(short[]),                      PrototypeFieldType.ListInt16 },
            { typeof(int[]),                        PrototypeFieldType.ListInt32 },
            { typeof(long[]),                       PrototypeFieldType.ListInt64 },
            { typeof(float[]),                      PrototypeFieldType.ListFloat32 },
            { typeof(double[]),                     PrototypeFieldType.ListFloat64 },
            { typeof(Enum[]),                       PrototypeFieldType.ListEnum },
            { typeof(AssetId[]),                    PrototypeFieldType.ListAssetRef },
            { typeof(AssetTypeId[]),                PrototypeFieldType.ListAssetTypeRef },
            { typeof(PrototypeId[]),                PrototypeFieldType.ListPrototypeDataRef },
            { typeof(Prototype[]),                  PrototypeFieldType.ListPrototypePtr },
            { typeof(PrototypeMixinList),           PrototypeFieldType.ListMixin },
            { typeof(PrototypePropertyCollection),  PrototypeFieldType.PropertyCollection },
        };

        private readonly List<PrototypeFieldInfo> _fieldInfoTable = new();
        private readonly Dictionary<string, PrototypeFieldInfo> _fieldsByName = new();

        private readonly List<PrototypeFieldInfo> _postProcessableFields = new();

        public PrototypeFieldInfo PropertyCollection { get; }

        public IReadOnlyList<PrototypeFieldInfo> FieldInfoTable { get => _fieldInfoTable; }
        public IReadOnlyList<PrototypeFieldInfo> PostProcessableFields { get => _postProcessableFields; }

        public PrototypeFieldSet(Type type)
        {
            Debug.Assert(type == typeof(Prototype) || type.IsSubclassOf(typeof(Prototype)));

            // NOTE: Without BindingFlags.DeclaredOnly this will include all base class properties as well.
            foreach (System.Reflection.PropertyInfo propertyInfo in type.GetProperties())
            {
                if (propertyInfo.DeclaringType == typeof(Prototype))
                    continue;

                PrototypeFieldType fieldType = DeterminePrototypeFieldType(propertyInfo);
                if (fieldType == PrototypeFieldType.Invalid)
                    continue;

                PrototypeFieldInfo fieldInfo = new(propertyInfo, fieldType);
                _fieldInfoTable.Add(fieldInfo);
                _fieldsByName.Add(fieldInfo.Name, fieldInfo);

                switch (fieldType)
                {
                    case PrototypeFieldType.Mixin:
                    case PrototypeFieldType.PrototypePtr:
                    case PrototypeFieldType.ListPrototypePtr:
                    case PrototypeFieldType.ListMixin:
                        _postProcessableFields.Add(fieldInfo);
                        break;

                    case PrototypeFieldType.PropertyCollection:
                    case PrototypeFieldType.PropertyList:
                        Verify.IsTrue(PropertyCollection == null);  // There shouldn't be more than one PropertyCollection field per prototype
                        PropertyCollection = fieldInfo;
                        break;
                }
            }
        }

        public List<PrototypeFieldInfo>.Enumerator GetEnumerator()
        {
            return _fieldInfoTable.GetEnumerator();
        }

        public PrototypeFieldInfo GetFieldInfo(string fieldName)
        {
            if (_fieldsByName.TryGetValue(fieldName, out PrototypeFieldInfo fieldInfo) == false)
                return null;

            return fieldInfo;
        }

        public PrototypeFieldInfo GetMixinFieldInfo(Type fieldClassType, PrototypeFieldType fieldType)
        {
            foreach (PrototypeFieldInfo fieldInfo in _fieldInfoTable)
            {
                if (fieldType == PrototypeFieldType.Mixin)
                {
                    // For simple mixins we just return the mixin field that matches our class type
                    if (fieldInfo.Type == PrototypeFieldType.Mixin && fieldInfo.ClassType == fieldClassType)
                        return fieldInfo;
                }
                else if (fieldType == PrototypeFieldType.ListMixin)
                {
                    // For list mixins we look for a list that is compatible with our requested field type

                    // NOTE: While we check if the field type defined in the attribute matches our field class type argument exactly,
                    // the client checks if the argument type is derived from the type defined in the field info.
                    // This doesn't seem to cause any issues in 1.52, but may need to be changed if we run into issues with other versions.

                    if (fieldInfo.Type == PrototypeFieldType.ListMixin && fieldInfo.ListElementType == fieldClassType)
                        return fieldInfo;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines a matching <see cref="PrototypeFieldType"/> enum value for a <see cref="System.Reflection.PropertyInfo"/>.
        /// </summary>
        private static PrototypeFieldType DeterminePrototypeFieldType(System.Reflection.PropertyInfo propertyInfo)
        {
            // Check if we have an explicit type field definition via an attribute.
            // This includes properties flagged with [DoNotCopy], which is a shorthand for specifying PrototypeFieldType.Invalid.
            PrototypeFieldAttribute prototypeFieldAttribute = propertyInfo.GetCustomAttribute<PrototypeFieldAttribute>();
            if (prototypeFieldAttribute != null)
                return prototypeFieldAttribute.Type;

            Type fieldClassType = propertyInfo.PropertyType;

            // Manually determine some of non-primitive types
            if (fieldClassType.IsPrimitive == false)
            {
                if (fieldClassType.IsArray == false)
                {
                    // Check for prototypes and asset enums
                    // In resource prototypes we consider embedded prototypes as PrototypeFieldType.PrototypePtr (same as Calligraphy),
                    // even though technically they should be just PrototypeFieldType.Prototype. Distinguishing them doesn't seem
                    // to serve any purpose within our implementation of this system as of right now.
                    if (fieldClassType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.PrototypePtr;
                    else if (fieldClassType.IsEnum && fieldClassType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.Enum;
                }
                else
                {
                    // Check element type instead if it's a collection
                    Type elementType = fieldClassType.GetElementType();

                    if (elementType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.ListPrototypePtr;
                    else if (elementType.IsEnum && elementType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.ListEnum;
                }
            }

            // Try to match a C# type to a prototype field type enum value using a lookup.
            if (TypeToPrototypeFieldTypeEnumLookup.TryGetValue(fieldClassType, out PrototypeFieldType prototypeFieldTypeEnumValue) == false)
                return PrototypeFieldType.Invalid;

            return prototypeFieldTypeEnumValue;
        }
    }
}
