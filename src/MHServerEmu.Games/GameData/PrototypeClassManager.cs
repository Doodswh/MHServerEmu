using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using MHServerEmu.Core.Helpers;
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
        private readonly Dictionary<uint, Type> _prototypeTypesByHash;
        private readonly Dictionary<Type, Func<Prototype>> _prototypeConstructors;
        private readonly Dictionary<Type, PrototypeFieldSet> _prototypeFieldSets;

        public int ClassCount { get => _prototypeTypes.Count; }

        public PrototypeClassManager()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (PrototypeClassIsA(type, typeof(Prototype)))
                    _prototypeTypes.Add(type.Name, type);
            }

            int numPrototypeClasses = _prototypeTypes.Count;
            _prototypeTypesByHash = new(numPrototypeClasses);
            _prototypeConstructors = new(numPrototypeClasses);
            _prototypeFieldSets = new(numPrototypeClasses);

            // djb2 hashes are used to identify classes in resource prototypes.
            foreach (Type type in _prototypeTypes.Values)
                _prototypeTypesByHash.Add(HashHelper.Djb2(type.Name), type);

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
        /// <remarks>
        /// This replaces PrototypeClassManager::GetPrototypeClassInfoByName() from the client.
        /// </remarks>
        public Type GetPrototypeClassTypeByName(string name)
        {
            if (_prototypeTypes.TryGetValue(name, out Type type) == false)
                return null;

            return type;
        }

        /// <summary>
        /// Gets prototype class type by the djb2 hash of its name.
        /// </summary>
        /// <remarks>
        /// This replaces PrototypeClassManager::GetPrototypeClassInfoByNameHash() from the client.
        /// </remarks>
        public Type GetPrototypeClassTypeByNameHash(uint hash)
        {
            if (_prototypeTypesByHash.TryGetValue(hash, out Type type) == false)
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
        /// Returns a Enumerator of all prototype class types.
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
        public PrototypeFieldInfo GetFieldInfo(Type prototypeClassType, BlueprintMemberInfo blueprintMemberInfo, bool getPropertyCollection)
        {
            if (getPropertyCollection)
            {
                // We don't need a loop like the client because our PrototypeFieldSet implementation includes fields from base classes.
                PrototypeFieldSet fieldSet = GetPrototypeFieldSet(prototypeClassType);
                return fieldSet?.PropertyCollection;
            }

            // Return the C# property info the blueprint member is bound to if we are not looking for a property collection
            return blueprintMemberInfo.Member?.RuntimeClassFieldInfo;
        }

        /// <summary>
        /// Returns a <see cref="PrototypeFieldInfo"/> for a mixin field in a Calligraphy prototype.
        /// </summary>
        public PrototypeFieldInfo GetMixinFieldInfo(Type ownerClassType, Type fieldClassType, PrototypeFieldType fieldType)
        {
            // We don't need a loop like the client because our PrototypeFieldSet implementation includes fields from base classes.
            PrototypeFieldSet fieldSet = GetPrototypeFieldSet(ownerClassType);
            return fieldSet?.GetMixinFieldInfo(fieldClassType, fieldType);
        }

        public PrototypeFieldSet GetPrototypeFieldSet(Type type)
        {
            if (_prototypeFieldSets.TryGetValue(type, out PrototypeFieldSet fieldSet) == false)
            {
                fieldSet = new(type);
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

            PrototypeFieldSet fieldSet = GetPrototypeFieldSet(prototype.GetType());
            if (!Verify.IsNotNull(fieldSet)) return;

            IReadOnlyList<PrototypeFieldInfo> postProcessableFields = fieldSet.PostProcessableFields;
            int numPostProcessableFields = postProcessableFields.Count;

            for (int j = 0; j < numPostProcessableFields; j++)
            {
                PrototypeFieldInfo fieldInfo = postProcessableFields[j];

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
    }
}
