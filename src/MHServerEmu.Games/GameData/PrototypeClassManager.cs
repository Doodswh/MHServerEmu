using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.PatchManager;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData
{
    // We use C# types and reflection instead of class ids / class info and GRTTI

    public class PrototypeClassManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly Dictionary<string, Type> _prototypeTypes = new();
        private readonly Dictionary<Type, Func<Prototype>> _prototypeConstructors;

        private readonly Dictionary<(Type, string), PrototypeFieldInfo> _prototypeFieldInfos = new();
        private readonly Dictionary<Type, List<PrototypeFieldInfo>> _prototypeFieldSets = new();
        private readonly Dictionary<Type, List<PrototypeFieldInfo>> _postProcessableFields = new();

        private static readonly Dictionary<Type, PrototypeFieldType> TypeToPrototypeFieldTypeEnumLookup = new()
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
            { typeof(PrototypePropertyCollection),  PrototypeFieldType.PropertyCollection },
        };

        public int ClassCount { get => _prototypeTypes.Count; }

        public PrototypeClassManager()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (PrototypeClassIsA(type, typeof(Prototype)))
                    _prototypeTypes.Add(type.Name, type);
            }

            _prototypeConstructors = new(_prototypeTypes.Count);

            stopwatch.Stop();
            Logger.Info($"Initialized {_prototypeTypes.Count} prototype classes in {stopwatch.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Creates a new <see cref="Prototype"/> instance of the specified <see cref="Type"/> using a cached constructor delegate if possible.
        /// </summary>
        public Prototype AllocatePrototype(Type type)
        {
            // Check if we already have a cached constructor delegate
            if (_prototypeConstructors.TryGetValue(type, out Func<Prototype> constructor) == false)
            {
                // Cache constructor delegate for future use
                DynamicMethod dm = new("ConstructPrototype", typeof(Prototype), null);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
                il.Emit(OpCodes.Ret);

                constructor = dm.CreateDelegate<Func<Prototype>>();
                _prototypeConstructors.Add(type, constructor);
            }

            return constructor();
        }

        /// <summary>
        /// Gets prototype class type by its name.
        /// </summary>
        public Type GetPrototypeClassTypeByName(string name)
        {
            if (_prototypeTypes.TryGetValue(name, out Type type) == false)
                return null;

            return type;
        }

        /// <summary>
        /// Checks if a prototype class belongs to the specified parent class in the hierarchy.
        /// </summary>
        public bool PrototypeClassIsA(Type classToCheck, Type parent)
        {
            return classToCheck == parent || classToCheck.IsSubclassOf(parent);
        }

        /// <summary>
        /// Returns an IEnumerable of all prototype class types.
        /// </summary>
        public Dictionary<string, Type>.ValueCollection.Enumerator GetEnumerator()
        {
            return _prototypeTypes.Values.GetEnumerator();
        }

        /// <summary>
        /// Determines what asset types to bind to what enums and 
        /// </summary>
        public void BindAssetTypesToEnums(AssetDirectory assetDirectory)
        {
            Dictionary<AssetType, Type> assetEnumBindings = new();

            // The client iterates all prototype types here to find symbolic enum bindings,
            // we just have everything we actually need in PropertyParamEnumLookups instead.

            // Add bindings explicitly defined in PropertyInfoTable
            foreach (var binding in PropertyInfoTable.PropertyParamEnumLookups)
            {
                AssetType assetType = assetDirectory.GetAssetType(binding.Name);
                assetEnumBindings.Add(assetType, binding.ClassType);
            }

            assetDirectory.BindAssetTypes(assetEnumBindings);
        }

        /// <summary>
        /// Returns a <see cref="PrototypeFieldInfo"/> for a field in a Calligraphy prototype.
        /// </summary>
        public PrototypeFieldInfo GetFieldInfo(Type prototypeClassType, BlueprintMemberInfo? blueprintMemberInfo, bool getPropertyCollection)
        {
            // Return the C# property info the blueprint member is bound to if we are not looking for a property collection
            if (getPropertyCollection == false)
                return blueprintMemberInfo?.Member.RuntimeClassFieldInfo;

            // Look for a property collection field for this prototype
            // Same as in CalligraphySerializer.GetPropertyCollection(), we make use of the fact that
            // all property collection fields in our data are called "Properties".
            // The client here iterates all fields to find the one that is the property collection.
            return GetFieldInfo(prototypeClassType, "Properties");
        }

        public PrototypeFieldInfo GetFieldInfo(Type prototypeClassType, string memberName)
        {
            var key = (prototypeClassType, memberName);

            if (_prototypeFieldInfos.TryGetValue(key, out PrototypeFieldInfo fieldInfo) == false)
            {
                fieldInfo = null;   // Cache the result of this lookup even if it fails to avoid doing it again.

                foreach (PrototypeFieldInfo itFieldInfo in GetPrototypeFieldSet(prototypeClassType))
                {
                    if (itFieldInfo.Name == memberName)
                    {
                        fieldInfo = itFieldInfo;
                        break;
                    }
                }

                _prototypeFieldInfos.Add(key, fieldInfo);
            }

            return fieldInfo;
        }

        /// <summary>
        /// Returns a <see cref="PrototypeFieldInfo"/> for a mixin field in a Calligraphy prototype.
        /// </summary>
        public PrototypeFieldInfo GetMixinFieldInfo(Type ownerClassType, Type fieldClassType, PrototypeFieldType fieldType)
        {
            // Make sure we have a valid field type enum value
            if ((fieldType == PrototypeFieldType.Mixin || fieldType == PrototypeFieldType.ListMixin) == false)
                throw new ArgumentException($"{fieldType} is not a mixin field type.");

            // Search the entire class hierarchy for a mixin of the matching type (not sure if this is actually needed with reflection)
            while (ownerClassType != typeof(Prototype))
            {
                // We do what PrototypeFieldSet::GetMixinFieldInfo() does right here using reflection
                foreach (PrototypeFieldInfo fieldInfo in GetPrototypeFieldSet(ownerClassType))
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

                // Go up in the hierarchy if not found
                ownerClassType = ownerClassType.BaseType;
            }

            // Mixin not found
            return null;
        }

        public List<PrototypeFieldInfo> GetPrototypeFieldSet(Type type)
        {
            if (_prototypeFieldSets.TryGetValue(type, out List<PrototypeFieldInfo> fieldSet) == false)
            {
                fieldSet = new();

                // NOTE: Without BindingFlags.DeclaredOnly this will include all base class properties as well.
                foreach (System.Reflection.PropertyInfo propertyInfo in type.GetProperties())
                {
                    if (propertyInfo.DeclaringType == typeof(Prototype))
                        continue;

                    PrototypeFieldType fieldType = DeterminePrototypeFieldType(propertyInfo);
                    if (fieldType == PrototypeFieldType.Invalid)
                        continue;

                    PrototypeFieldInfo fieldInfo = new(propertyInfo, fieldType);
                    fieldSet.Add(fieldInfo);
                }

                _prototypeFieldSets.Add(type, fieldSet);
            }

            return fieldSet;
        }

        public uint CalculateDataCRC(Prototype prototype)
        {
            // Since we don't have version migration, we can get away with using just the prototype's path crc for now.
            return (uint)((ulong)prototype.DataRef >> 32);
        }

        /// <summary>
        /// Calls PostProcess() on all prototypes embedded in the provided one.
        /// </summary>
        public void PostProcessContainedPrototypes(Prototype prototype)
        {
            bool hasPatch = PrototypePatchManager.Instance.PreCheck(prototype.DataRef);

            foreach (PrototypeFieldInfo fieldInfo in GetPostProcessablePrototypeFields(prototype.GetType()))
            {
                switch (fieldInfo.Type)
                {
                    case PrototypeFieldType.PrototypePtr:
                    case PrototypeFieldType.Mixin:
                        // Simple embedded prototypes
                        fieldInfo.GetValue(prototype, out Prototype embeddedPrototype);
                        if (embeddedPrototype != null)
                        {
                            if (hasPatch) PrototypePatchManager.Instance.SetPath(prototype, embeddedPrototype, fieldInfo.Name);
                            embeddedPrototype.PostProcess();
                        }
                        break;

                    case PrototypeFieldType.ListPrototypePtr:
                        // List / vector collections of embedded prototypes (that we implemented as arrays)
                        fieldInfo.GetValue(prototype, out IReadOnlyList<Prototype> prototypeCollection);
                        if (prototypeCollection == null)
                            continue;

                        int index = 0;
                        for (int i = 0; i < prototypeCollection.Count; i++)
                        {
                            Prototype element = prototypeCollection[i];
                            if (hasPatch) PrototypePatchManager.Instance.SetPathIndex(prototype, element, fieldInfo.Name, index++);
                            element.PostProcess();
                        }
                        
                        break;

                    case PrototypeFieldType.ListMixin:
                        fieldInfo.GetValue(prototype, out PrototypeMixinList mixinList);
                        if (mixinList == null)
                            continue;

                        foreach (PrototypeMixinListItem mixin in mixinList)
                            mixin.Prototype.PostProcess();
                        
                        break;
                }
            }

            if (hasPatch) PrototypePatchManager.Instance.PostOverride(prototype);
        }

        /// <summary>
        /// PreCheck data of prototype for patch.
        /// </summary>
        public void PreCheck(Prototype prototype)
        {
            bool hasPatch = PrototypePatchManager.Instance.PreCheck(prototype.DataRef);
            if (hasPatch) PrototypePatchManager.Instance.PostOverride(prototype);
        }

        private List<PrototypeFieldInfo> GetPostProcessablePrototypeFields(Type type)
        {
            if (_postProcessableFields.TryGetValue(type, out List<PrototypeFieldInfo> postProcessableFields) == false)
            {
                postProcessableFields = new();

                foreach (PrototypeFieldInfo fieldInfo in GetPrototypeFieldSet(type))
                {
                    switch (fieldInfo.Type)
                    {
                        case PrototypeFieldType.PrototypePtr:
                        case PrototypeFieldType.Mixin:
                        case PrototypeFieldType.ListPrototypePtr:
                        case PrototypeFieldType.ListMixin:
                            postProcessableFields.Add(fieldInfo);
                            break;
                    }
                }

                _postProcessableFields.Add(type, postProcessableFields);
            }

            return postProcessableFields;
        }

        /// <summary>
        /// Determines a matching <see cref="PrototypeFieldType"/> enum value for a <see cref="System.Reflection.PropertyInfo"/>.
        /// </summary>
        private static PrototypeFieldType DeterminePrototypeFieldType(System.Reflection.PropertyInfo fieldInfo)
        {
            // Check if we have an explicit type field definition via an attribute.
            // This includes properties flagged with [DoNotCopy], which is a shorthand for specifying PrototypeFieldType.Invalid.
            PrototypeFieldAttribute prototypeFieldAttribute = fieldInfo.GetCustomAttribute<PrototypeFieldAttribute>();
            if (prototypeFieldAttribute != null)
                return prototypeFieldAttribute.Type;

            Type fieldType = fieldInfo.PropertyType;

            // Manually determine some of non-primitive types
            if (fieldType.IsPrimitive == false)
            {
                if (fieldType.IsArray == false)
                {
                    // Check for prototypes and asset enums
                    // In resource prototypes we consider embedded prototypes as PrototypeFieldType.PrototypePtr (same as Calligraphy),
                    // even though technically they should be just PrototypeFieldType.Prototype. Distinguishing them doesn't seem
                    // to serve any purpose within our implementation of this system as of right now.
                    if (fieldType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.PrototypePtr;
                    else if (fieldType.IsEnum && fieldType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.Enum;
                }
                else
                {
                    // Check element type instead if it's a collection
                    Type elementType = fieldType.GetElementType();

                    if (elementType.IsSubclassOf(typeof(Prototype)))
                        return PrototypeFieldType.ListPrototypePtr;
                    else if (elementType.IsEnum && elementType.IsDefined(typeof(AssetEnumAttribute)))
                        return PrototypeFieldType.ListEnum;
                }
            }

            // Try to match a C# type to a prototype field type enum value using a lookup dict
            if (TypeToPrototypeFieldTypeEnumLookup.TryGetValue(fieldType, out PrototypeFieldType prototypeFieldTypeEnumValue) == false)
                return PrototypeFieldType.Invalid;

            return prototypeFieldTypeEnumValue;
        }
    }
}
