using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Calligraphy.Attributes;
using MHServerEmu.Games.GameData.PatchManager;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData
{
    public class PrototypeClassManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly Dictionary<string, Type> _prototypeNameToClassTypeDict = new();
        private readonly Dictionary<Type, Func<Prototype>> _prototypeConstructorDict;
        private readonly Dictionary<System.Reflection.PropertyInfo, PrototypeFieldType> _prototypeFieldTypeDict = new();

        private readonly Dictionary<Type, CachedPrototypeField[]> _copyableFieldDict = new();
        private readonly Dictionary<Type, CachedPrototypeField[]> _postProcessableFieldDict = new();

        private readonly Dictionary<Type, EnhancedFieldInfo[]> _enhancedFieldCache = new();
        private readonly Dictionary<Type, Dictionary<string, EnhancedFieldInfo>> _fieldNameLookupCache = new();

        private static readonly Dictionary<Type, PrototypeFieldType> TypeToPrototypeFieldTypeEnumDict = new()
        {
            { typeof(bool),                         PrototypeFieldType.Bool },
            { typeof(sbyte),                        PrototypeFieldType.Int8 },
            { typeof(short),                        PrototypeFieldType.Int16 },
            { typeof(int),                          PrototypeFieldType.Int32 },
            { typeof(long),                         PrototypeFieldType.Int64 },
            { typeof(float),                        PrototypeFieldType.Float32 },
            { typeof(double),                       PrototypeFieldType.Float64 },
            { typeof(Enum),                         PrototypeFieldType.Enum },
            { typeof(AssetId),                      PrototypeFieldType.AssetRef },
            { typeof(AssetTypeId),                  PrototypeFieldType.AssetTypeRef },
            { typeof(CurveId),                      PrototypeFieldType.CurveRef },
            { typeof(PrototypeId),                  PrototypeFieldType.PrototypeDataRef },
            { typeof(LocaleStringId),               PrototypeFieldType.LocaleStringId },
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
            { typeof(PrototypePropertyCollection),  PrototypeFieldType.PropertyCollection }
        };

        public int ClassCount { get => _prototypeNameToClassTypeDict.Count; }

        public PrototypeClassManager()
        {
            var stopwatch = Stopwatch.StartNew();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (PrototypeClassIsA(type, typeof(Prototype)) == false) continue;
                _prototypeNameToClassTypeDict.Add(type.Name, type);
            }

            _prototypeConstructorDict = new(ClassCount);

            stopwatch.Stop();
            Logger.Info($"Initialized {ClassCount} prototype classes in {stopwatch.ElapsedMilliseconds} ms");
        }

        public Prototype AllocatePrototype(Type type)
        {
            if (_prototypeConstructorDict.TryGetValue(type, out var constructorDelegate) == false)
            {
                DynamicMethod dm = new("ConstructPrototype", typeof(Prototype), null);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
                il.Emit(OpCodes.Ret);

                constructorDelegate = dm.CreateDelegate<Func<Prototype>>();
                _prototypeConstructorDict.Add(type, constructorDelegate);
            }

            return constructorDelegate();
        }

        public Type GetPrototypeClassTypeByName(string name)
        {
            if (_prototypeNameToClassTypeDict.TryGetValue(name, out Type type) == false)
            {
                Logger.Warn($"Prototype class {name} not found");
                return null;
            }

            return type;
        }

        public bool PrototypeClassIsA(Type classToCheck, Type parent)
        {
            return classToCheck == parent || classToCheck.IsSubclassOf(parent);
        }

        public IEnumerable<Type> GetEnumerator()
        {
            return _prototypeNameToClassTypeDict.Values.AsEnumerable();
        }

        public void BindAssetTypesToEnums(AssetDirectory assetDirectory)
        {
            Dictionary<AssetType, Type> assetEnumBindingDict = new();
            foreach (var binding in PropertyInfoTable.AssetEnumBindings)
            {
                AssetType assetType = assetDirectory.GetAssetType(binding.Item1);
                assetEnumBindingDict.Add(assetType, binding.Item2);
            }
            assetDirectory.BindAssetTypes(assetEnumBindingDict);
        }

        public System.Reflection.PropertyInfo GetFieldInfo(Type prototypeClassType, BlueprintMemberInfo? blueprintMemberInfo, bool getPropertyCollection)
        {
            if (getPropertyCollection == false)
                return blueprintMemberInfo?.Member.RuntimeClassFieldInfo;

            return prototypeClassType.GetProperty("Properties");
        }

        public System.Reflection.PropertyInfo GetMixinFieldInfo(Type ownerClassType, Type fieldClassType, PrototypeFieldType fieldType)
        {
            if ((fieldType == PrototypeFieldType.Mixin || fieldType == PrototypeFieldType.ListMixin) == false)
                throw new ArgumentException($"{fieldType} is not a mixin field type.");

            while (ownerClassType != typeof(Prototype))
            {
                foreach (var property in ownerClassType.GetProperties())
                {
                    if (fieldType == PrototypeFieldType.Mixin)
                    {
                        if (property.PropertyType != fieldClassType) continue;
                        if (property.IsDefined(typeof(MixinAttribute))) return property;
                    }
                    else if (fieldType == PrototypeFieldType.ListMixin)
                    {
                        if (property.PropertyType != typeof(PrototypeMixinList)) continue;

                        var attribute = property.GetCustomAttribute<ListMixinAttribute>();
                        if (attribute.FieldType == fieldClassType)
                            return property;
                    }
                }

                ownerClassType = ownerClassType.BaseType;
            }

            return null;
        }

        public PrototypeFieldType GetPrototypeFieldTypeEnumValue(System.Reflection.PropertyInfo fieldInfo)
        {
            if (_prototypeFieldTypeDict.TryGetValue(fieldInfo, out var prototypeFieldTypeEnumValue) == false)
            {
                prototypeFieldTypeEnumValue = DeterminePrototypeFieldType(fieldInfo);
                _prototypeFieldTypeDict.Add(fieldInfo, prototypeFieldTypeEnumValue);
            }

            return prototypeFieldTypeEnumValue;
        }

        public CachedPrototypeField[] GetCopyablePrototypeFields(Type type)
        {
            if (_copyableFieldDict.TryGetValue(type, out CachedPrototypeField[] copyableFields) == false)
            {
                List<CachedPrototypeField> copyableFieldList = new();

                foreach (var fieldInfo in type.GetProperties())
                {
                    if (fieldInfo.DeclaringType == typeof(Prototype))
                        continue;

                    PrototypeFieldType fieldType = GetPrototypeFieldTypeEnumValue(fieldInfo);
                    if (fieldType == PrototypeFieldType.Invalid)
                        continue;

                    copyableFieldList.Add(new(fieldInfo, fieldType));
                }

                copyableFields = copyableFieldList.ToArray();
                _copyableFieldDict.Add(type, copyableFields);
            }

            return copyableFields;
        }

        public uint CalculateDataCRC(Prototype prototype)
        {
            return (uint)((ulong)prototype.DataRef >> 32);
        }

        public void PostProcessContainedPrototypes(Prototype prototype)
        {
            bool hasPatch = PrototypePatchManager.Instance.PreCheck(prototype.DataRef);

            foreach (CachedPrototypeField cachedField in GetPostProcessablePrototypeFields(prototype.GetType()))
            {
                System.Reflection.PropertyInfo fieldInfo = cachedField.FieldInfo;

                switch (cachedField.FieldType)
                {
                    case PrototypeFieldType.PrototypePtr:
                    case PrototypeFieldType.Mixin:
                        var embeddedPrototype = (Prototype)fieldInfo.GetValue(prototype);
                        if (embeddedPrototype != null)
                        {
                            if (hasPatch) PrototypePatchManager.Instance.SetPath(prototype, embeddedPrototype, fieldInfo.Name);
                            embeddedPrototype.PostProcess();
                        }
                        break;

                    case PrototypeFieldType.ListPrototypePtr:
                        var prototypeCollection = (IEnumerable<Prototype>)fieldInfo.GetValue(prototype);
                        if (prototypeCollection == null) continue;

                        int index = 0;
                        foreach (Prototype element in prototypeCollection)
                        {
                            if (hasPatch) PrototypePatchManager.Instance.SetPathIndex(prototype, element, fieldInfo.Name, index++);
                            element.PostProcess();
                        }

                        break;

                    case PrototypeFieldType.ListMixin:
                        var mixinList = (PrototypeMixinList)fieldInfo.GetValue(prototype);
                        if (mixinList == null) continue;

                        foreach (PrototypeMixinListItem mixin in mixinList)
                            mixin.Prototype.PostProcess();

                        break;
                }
            }

            if (hasPatch) PrototypePatchManager.Instance.PostOverride(prototype);
        }

        public void PreCheck(Prototype prototype)
        {
            bool hasPatch = PrototypePatchManager.Instance.PreCheck(prototype.DataRef);
            if (hasPatch) PrototypePatchManager.Instance.PostOverride(prototype);
        }

   
        /// <summary>
        /// Gets enhanced field metadata for all patchable fields in a prototype type.
        /// Caches results for performance.
        /// </summary>
        public EnhancedFieldInfo[] GetPatchableFields(Type type)
        {
            if (_enhancedFieldCache.TryGetValue(type, out var cached))
                return cached;

            var fieldList = new List<EnhancedFieldInfo>();

            foreach (var propInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propInfo.DeclaringType == typeof(Prototype) &&
                    propInfo.Name != "DataRef" &&
                    propInfo.Name != "ParentDataRef")
                    continue;

                var fieldType = GetPrototypeFieldTypeEnumValue(propInfo);

                var enhanced = new EnhancedFieldInfo(propInfo, fieldType);
                fieldList.Add(enhanced);
            }

            var result = fieldList.ToArray();
            _enhancedFieldCache[type] = result;

            var nameLookup = new Dictionary<string, EnhancedFieldInfo>();
            foreach (var field in result)
                nameLookup[field.PropertyInfo.Name] = field;
            _fieldNameLookupCache[type] = nameLookup;

            return result;
        }

        /// <summary>
        /// Fast lookup for a field by name on a specific type.
        /// </summary>
        public EnhancedFieldInfo? GetFieldByName(Type type, string fieldName)
        {
            if (!_fieldNameLookupCache.TryGetValue(type, out var lookup))
            {
                GetPatchableFields(type);
                lookup = _fieldNameLookupCache[type];
            }

            return lookup.TryGetValue(fieldName, out var field) ? field : null;
        }

        /// <summary>
        /// Sets a value on a property, handling read-only properties via backing fields.
        /// </summary>
        public bool TrySetPropertyValue(object target, System.Reflection.PropertyInfo propInfo, object value)
        {
            try
            {
                if (propInfo.CanWrite)
                {
                    propInfo.SetValue(target, value);
                    return true;
                }
                var backingField = target.GetType().GetField(
                    $"<{propInfo.Name}>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (backingField != null)
                {
                    backingField.SetValue(target, value);
                    return true;
                }

                Logger.Warn($"Cannot set read-only property '{propInfo.Name}' - no backing field found");
                return false;
            }
            catch (Exception ex)
            {
                Logger.ErrorException(ex, $"Failed to set property '{propInfo.Name}'");
                return false;
            }
        }

        /// <summary>
        /// Gets all property names available on a type (for debugging/validation).
        /// </summary>
        public IEnumerable<string> GetAvailablePropertyNames(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name);
        }

        /// <summary>
        /// Validates if a path can be resolved on a prototype type.
        /// Returns null if valid, or an error message if invalid.
        /// </summary>
        public string ValidateFieldPath(Type rootType, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('.');
            var currentType = rootType;

            foreach (var part in parts)
            {
                // Strip array indices for validation
                var fieldName = part.Contains('[') ? part.Substring(0, part.IndexOf('[')) : part;

                var propInfo = currentType.GetProperty(fieldName);
                if (propInfo == null)
                {
                    var available = string.Join(", ", GetAvailablePropertyNames(currentType));
                    return $"Field '{fieldName}' not found on type '{currentType.Name}'. Available: {available}";
                }

                // Update current type for next iteration
                currentType = propInfo.PropertyType;

                // Handle array element types
                if (currentType.IsArray)
                    currentType = currentType.GetElementType();
                else if (currentType == typeof(PrototypeMixinList))
                    break; // Can't validate further into mixin lists
            }

            return null; // Valid path
        }

        private CachedPrototypeField[] GetPostProcessablePrototypeFields(Type type)
        {
            if (_postProcessableFieldDict.TryGetValue(type, out CachedPrototypeField[] postProcessableFields) == false)
            {
                List<CachedPrototypeField> postProcessableFieldList = new();

                foreach (var fieldInfo in type.GetProperties())
                {
                    if (fieldInfo.DeclaringType == typeof(Prototype))
                        continue;

                    PrototypeFieldType fieldType = GetPrototypeFieldTypeEnumValue(fieldInfo);

                    switch (fieldType)
                    {
                        case PrototypeFieldType.PrototypePtr:
                        case PrototypeFieldType.Mixin:
                        case PrototypeFieldType.ListPrototypePtr:
                        case PrototypeFieldType.ListMixin:
                            postProcessableFieldList.Add(new(fieldInfo, fieldType));
                            break;
                    }
                }

                postProcessableFields = postProcessableFieldList.ToArray();
                _postProcessableFieldDict.Add(type, postProcessableFields);
            }

            return postProcessableFields;
        }

        private PrototypeFieldType DeterminePrototypeFieldType(System.Reflection.PropertyInfo fieldInfo)
        {
            if (fieldInfo.IsDefined(typeof(DoNotCopyAttribute)))
                return PrototypeFieldType.Invalid;

            var fieldType = fieldInfo.PropertyType;

            if (fieldType.IsPrimitive == false)
            {
                if (fieldType.IsArray == false)
                {
                    if (fieldType == typeof(LocomotorPrototype) || fieldType == typeof(PopulationInfoPrototype) || fieldType == typeof(ProductPrototype))
                        return PrototypeFieldType.Mixin;
                    else if (fieldType == typeof(PrototypeMixinList))
                        return PrototypeFieldType.ListMixin;

                    if (fieldType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.PrototypePtr;
                    else if (fieldType.IsEnum && fieldType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.Enum;
                }
                else
                {
                    var elementType = fieldType.GetElementType();

                    if (elementType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.ListPrototypePtr;
                    else if (elementType.IsEnum && elementType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.ListEnum;
                }
            }

            if (TypeToPrototypeFieldTypeEnumDict.TryGetValue(fieldType, out var prototypeFieldTypeEnumValue) == false)
                return PrototypeFieldType.Invalid;

            return prototypeFieldTypeEnumValue;
        }

        // ============================
        // Enhanced Field Info Struct
        // ============================

        public readonly struct EnhancedFieldInfo
        {
            public readonly System.Reflection.PropertyInfo PropertyInfo;
            public readonly PrototypeFieldType FieldType;
            public readonly bool IsWritable;
            public readonly bool IsArray;
            public readonly Type ElementType;
            public readonly bool HasBackingField;
            public readonly FieldInfo BackingField;

            public EnhancedFieldInfo(System.Reflection.PropertyInfo propInfo, PrototypeFieldType fieldType)
            {
                PropertyInfo = propInfo;
                FieldType = fieldType;
                IsWritable = propInfo.CanWrite;
                IsArray = propInfo.PropertyType.IsArray;
                ElementType = IsArray ? propInfo.PropertyType.GetElementType() : propInfo.PropertyType;

                // Check for backing field
                BackingField = propInfo.DeclaringType?.GetField(
                    $"<{propInfo.Name}>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                HasBackingField = BackingField != null;
            }

            public bool CanWrite => IsWritable || HasBackingField;
        }

        public readonly struct CachedPrototypeField
        {
            public readonly System.Reflection.PropertyInfo FieldInfo;
            public readonly PrototypeFieldType FieldType;

            public CachedPrototypeField(System.Reflection.PropertyInfo fieldInfo, PrototypeFieldType fieldType)
            {
                FieldInfo = fieldInfo;
                FieldType = fieldType;
            }
        }
    }
}