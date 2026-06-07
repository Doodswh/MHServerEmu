using System.Reflection;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    /// <summary>
    /// Defines field groups (data schemas) for Calligraphy prototypes.
    /// </summary>
    public class Blueprint
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly Dictionary<StringId, BlueprintMember> _members;        // Field definitions for prototypes that use this blueprint  

        private PrototypeId[] _enumValueToPrototypeLookup = Array.Empty<PrototypeId>();
        private Dictionary<PrototypeId, int> _prototypeToEnumValueLookup;

        public BlueprintId Id { get; }
        public BlueprintGuid Guid { get; }

        public HashSet<BlueprintId> FileIds { get; } = new();                   // Contains ids of all blueprints related to this one in the hierarchy
        public List<PrototypeDataRefRecord> PrototypeRecords { get; } = new();  // A list of all prototype records that use this blueprint for iteration

        public Type RuntimeBindingClassType { get; }                            // Type of the class that handles prototypes that use this blueprint
        public PrototypeId DefaultPrototypeRef { get; }                         // .defaults prototype file id
        public BlueprintReference[] Parents { get; }
        public BlueprintReference[] ContributingBlueprints { get; }

        public PrototypeId PropertyPrototypeRef { get; private set; } = PrototypeId.Invalid;

        public int PrototypeMaxEnumValue { get => _enumValueToPrototypeLookup.Length - 1; }

        /// <summary>
        /// Deserializes a new <see cref="Blueprint"/> instance from a <see cref="Stream"/>.
        /// </summary>
        public Blueprint(Stream stream, BlueprintId id, BlueprintGuid guid)
        {
            Id = id;
            Guid = guid;

            // Deserialize
            using (BinaryReader reader = new(stream))
            {
                CalligraphyHeader header = new(reader);

                // Read runtime binding name and get a matching prototype class type from the prototype class manager
                string runtimeBinding = reader.ReadFixedString16();
                RuntimeBindingClassType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByName(runtimeBinding);
                
                DefaultPrototypeRef = (PrototypeId)reader.ReadUInt64();

                Parents = new BlueprintReference[reader.ReadInt16()];
                for (int i = 0; i < Parents.Length; i++)
                    Parents[i] = new(reader);

                ContributingBlueprints = new BlueprintReference[reader.ReadInt16()];
                for (int i = 0; i < ContributingBlueprints.Length; i++)
                    ContributingBlueprints[i] = new(reader);

                // Deserialize members
                short numMembers = reader.ReadInt16();
                _members = new(numMembers);
                for (int i = 0; i < numMembers; i++)
                {
                    BlueprintMember member = new(reader);
                    _members.Add(member.FieldId, member);
                }
            }

            // Bind non-property blueprint members to C# properties
            foreach (var member in _members.Values)
            {
                Type classBinding = RuntimeBindingClassType;
                while (classBinding != typeof(Prototype))
                {
                    // Try to find a matching property info in our runtime binding
                    member.RuntimeClassFieldInfo = classBinding.GetProperty(member.FieldName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
                    if (member.RuntimeClassFieldInfo != null) break;

                    // Go up in the hierarchy if we didn't find it
                    classBinding = classBinding.BaseType;
                }
            }
        }

        /// <summary>
        /// Gets a struct that contains a reference to a <see cref="BlueprintMember"/> and the <see cref="Blueprint"/> it belongs to.
        /// This method searches this blueprint, as well as all of its parents recursively.
        /// </summary>
        public bool TryGetBlueprintMemberInfo(StringId fieldId, out BlueprintMemberInfo memberInfo)
        {
            // Note: this is called GetBlueprintMemberInfo in the client, but we're calling it TryGetBlueprintMemberInfo here
            // to match the usual .NET naming conventions.
            
            // Check if the specified member belongs to this blueprint
            if (_members.TryGetValue(fieldId, out var member))
            {
                memberInfo = new(this, member);
                return true;
            }

            // Check if the specified member belongs to any of our parents
            foreach (BlueprintReference parentRef in Parents)
            {
                Blueprint parent = GameDatabase.GetBlueprint(parentRef.BlueprintId);
                if (parent.TryGetBlueprintMemberInfo(fieldId, out memberInfo))
                    return true;
            }

            // Fallback if no such member belongs to this blueprint
            memberInfo = default;
            return false;
        }

        /// <summary>
        /// Begins file id hash set population for this blueprint.
        /// </summary>
        public void OnAllDirectoriesLoaded()
        {
            // Data ref fixups happen here in the client - we don't really need those right now

            PopulateFileIds(FileIds);
        }

        /// <summary>
        /// Populates file id hash set for this blueprint. This should be called only from this or related blueprints.
        /// </summary>
        public void PopulateFileIds(HashSet<BlueprintId> callerFileIdHashSet)
        {
            // Begin building a new hash set if ours is empty
            if (FileIds.Count == 0)
            {
                FileIds.Add(Id);     // add this blueprint's id

                // Add parent ids
                foreach (BlueprintReference parentRef in Parents)
                {
                    var parent = GameDatabase.GetBlueprint(parentRef.BlueprintId);
                    parent.PopulateFileIds(FileIds);
                }
            }

            // Add this blueprint's hash set if it's a parent of the caller
            if (callerFileIdHashSet != FileIds)
            {
                foreach (BlueprintId id in FileIds)
                    callerFileIdHashSet.Add(id);
            }
        }

        /// <summary>
        /// Generates EnumValue -> PrototypeId and PrototypeId -> EnumValue lookups for this blueprint.
        /// </summary>
        public void GenerateEnumLookups()
        {
            // NOTE: Not present in the client, this is likely inlined in DataDirectory::initializeHierarchyCache() instead.

            int numRecords = PrototypeRecords.Count;
            int numLookups = numRecords + 1;

            // EnumValue -> PrototypeId
            _enumValueToPrototypeLookup = new PrototypeId[numLookups];
            _enumValueToPrototypeLookup[0] = PrototypeId.Invalid;

            _prototypeToEnumValueLookup = new(_enumValueToPrototypeLookup.Length);
            _prototypeToEnumValueLookup.Add(PrototypeId.Invalid, 0);

            for (int i = 0; i < numRecords; i++)
            {
                int enumValue = i + 1;
                PrototypeId prototypeDataRef = PrototypeRecords[i].PrototypeRef;

                _enumValueToPrototypeLookup[enumValue] = prototypeDataRef;
                _prototypeToEnumValueLookup.Add(prototypeDataRef, enumValue);
            }
        }

        /// <summary>
        /// Gets a <see cref="PrototypeId"/> for the specified enum value. Returns 0 if the enum value is out of range.
        /// </summary>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        public PrototypeId GetPrototypeFromEnumValue(int enumValue)
        {
            if (enumValue < 0 || enumValue >= _enumValueToPrototypeLookup.Length)
                return Logger.WarnReturn(PrototypeId.Invalid, $"Failed to get prototype for enumValue {enumValue} for blueprint {GameDatabase.GetBlueprintName(Id)}");

            return _enumValueToPrototypeLookup[enumValue];
        }

        /// <summary>
        /// Gets an enum value for the specified <see cref="PrototypeId"/>. Returns 0 if the prototype does not belong to this blueprint.
        /// </summary>
        public int GetPrototypeEnumValue(PrototypeId prototypeId)
        {
            if (_prototypeToEnumValueLookup.TryGetValue(prototypeId, out int enumValue) == false)
                return Logger.WarnReturn(0, $"Failed to get enum value for prototype {GameDatabase.GetPrototypeName(prototypeId)} for blueprint {GameDatabase.GetBlueprintName(Id)}");

            return enumValue;
        }

        /// <summary>
        /// Binds this blueprint to a property prototype.
        /// </summary>
        public void SetPropertyPrototypeDataRef(PrototypeId propertyDataRef)
        {
            if (PropertyPrototypeRef != PrototypeId.Invalid)
                Logger.Warn(string.Format("Trying to bind blueprint {0} to property {1}, but this blueprint is already bound to {2}",
                            GameDatabase.GetBlueprint(Id), GameDatabase.GetPrototypeName(propertyDataRef), GameDatabase.GetPrototypeName(PropertyPrototypeRef)));

            PropertyPrototypeRef = propertyDataRef;
        }

        /// <summary>
        /// Returns if this blueprint is bound to a property. 
        /// </summary>
        public bool IsProperty()
        {
            return PropertyPrototypeRef != PrototypeId.Invalid;
        }

        /// <summary>
        /// Checks if this blueprint belongs to the specified blueprint in the hierarchy.
        /// </summary>
        public bool IsA(BlueprintId blueprintId)
        {
            return FileIds.Contains(blueprintId);
        }

        /// <summary>
        /// Checks if this blueprint belongs to the specified blueprint in the hierarchy.
        /// </summary>
        public bool IsA(Blueprint parent)
        {
            if (parent == null) Logger.WarnReturn(false, "IsA() failed: parent is null");
            return IsA(parent.Id);
        }

        /// <summary>
        /// Checks if this blueprint is a child of the provided blueprint in the prototype class hierarchy. Blueprints are also considered children of themselves.
        /// </summary>
        public bool IsRuntimeChildOf(Blueprint parent)
        {
            // Check against itself
            if (parent == this) return true;

            // Check runtime bindings
            return GameDatabase.PrototypeClassManager.PrototypeClassIsA(RuntimeBindingClassType, parent.RuntimeBindingClassType);
        }

        /// <summary>
        /// Searches the blueprint hierarchy for a related blueprint that is bound to the specified class type.
        /// </summary>
        public Blueprint FindRuntimeBindingInBlueprintHierarchy(Type classType, Blueprint parentBlueprint)
        {
            if (RuntimeBindingClassType == classType && IsA(parentBlueprint)) return this;

            foreach (var parentRef in Parents)
            {
                Blueprint parent = GameDatabase.GetBlueprint(parentRef.BlueprintId);
                Blueprint result = parent.FindRuntimeBindingInBlueprintHierarchy(classType, parentBlueprint);
                if (result != null) return result;
            }

            return null;
        }

        public override string ToString() => GameDatabase.GetBlueprintName(Id);
    }

    /// <summary>
    /// Contains a reference to another blueprint.
    /// </summary>
    public readonly struct BlueprintReference
    {
        public BlueprintId BlueprintId { get; }
        public byte NumOfCopies { get; }

        /// <summary>
        /// Deserializes a <see cref="BlueprintReference"/>.
        /// </summary>
        public BlueprintReference(BinaryReader reader)
        {
            BlueprintId = (BlueprintId)reader.ReadUInt64();
            NumOfCopies = reader.ReadByte();
        }
    }

    /// <summary>
    /// Defines a field in a Calligraphy prototype.
    /// </summary>
    public class BlueprintMember
    {
        public StringId FieldId { get; }
        public string FieldName { get; }
        public CalligraphyBaseType BaseType { get; }
        public CalligraphyStructureType StructureType { get; }
        public ulong Subtype { get; }

        public PropertyInfo RuntimeClassFieldInfo { get; set; }     // This is C# reflection property info, not to be confused with entity properties

        /// <summary>
        /// Deserializes a new <see cref="BlueprintMember"/> instance.
        /// </summary>
        public BlueprintMember(BinaryReader reader)
        {
            FieldId = (StringId)reader.ReadUInt64();
            FieldName = reader.ReadFixedString16();
            BaseType = (CalligraphyBaseType)reader.ReadByte();
            StructureType = (CalligraphyStructureType)reader.ReadByte();

            switch (BaseType)
            {
                // Only these base types have subtypes
                case CalligraphyBaseType.Asset:
                case CalligraphyBaseType.Curve:
                case CalligraphyBaseType.Prototype:
                case CalligraphyBaseType.RHStruct:
                    Subtype = reader.ReadUInt64();
                    break;
            }
        }
    }

    /// <summary>
    /// Container for a blueprint member reference along with the blueprint it belongs to.
    /// </summary>
    public readonly struct BlueprintMemberInfo
    {
        public Blueprint Blueprint { get; }
        public BlueprintMember Member { get; }

        public BlueprintMemberInfo(Blueprint blueprint, BlueprintMember member)
        {
            Blueprint = blueprint;
            Member = member;
        }
    }
}
