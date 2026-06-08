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
        private Dictionary<StringId, BlueprintMember> _members;        // Field definitions for prototypes that use this blueprint  

        private PrototypeId[] _enumValueToPrototypeLookup = Array.Empty<PrototypeId>();
        private Dictionary<PrototypeId, int> _prototypeToEnumValueLookup;

        public BlueprintId BlueprintDataRef { get; private set; }
        public BlueprintGuid Guid { get; private set; }

        public HashSet<BlueprintId> FileIds { get; } = new();                   // Ids of all blueprints related to this one in the hierarchy
        public List<PrototypeDataRefRecord> PrototypeRecords { get; } = new();  // Prototype records that use this blueprint

        public Type RuntimeBindingClassType { get; private set; }               // Class that handles prototypes that use this blueprint
        public PrototypeId DefaultPrototypeRef { get; private set; }            // .defaults prototype file id
        public BlueprintId[] Parents { get; private set; }
        public BlueprintId[] ContributingBlueprints { get; private set; }

        public PrototypeId PropertyDataRef { get; private set; } = PrototypeId.Invalid;
        public bool IsProperty { get => PropertyDataRef != PrototypeId.Invalid; }

        public int PrototypeMaxEnumValue { get => _enumValueToPrototypeLookup.Length - 1; }

        /// <summary>
        /// Deserializes a new <see cref="Blueprint"/> instance from a <see cref="Stream"/>.
        /// </summary>
        public Blueprint() { }

        public override string ToString()
        {
            return GameDatabase.GetBlueprintName(BlueprintDataRef);
        }

        public bool Deserialize(BinaryReader dataReader, BlueprintGuid guid, BlueprintId blueprintRef)
        {
            BlueprintDataRef = blueprintRef;
            Guid = guid;

            try
            {
                CalligraphyHeader header = new(dataReader); // TODO: CalligraphyReader

                // RuntimeBinding
                string runtimeBinding = dataReader.ReadFixedString16();
                Type classType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByName(runtimeBinding);
                if (!Verify.IsNotNull(classType)) return false;
                RuntimeBindingClassType = classType;

                // DefaultPrototypeRef
                DefaultPrototypeRef = (PrototypeId)dataReader.ReadUInt64();

                // Parents
                short numParents = dataReader.ReadInt16();
                if (numParents > 0)
                {
                    Parents = new BlueprintId[numParents];
                    for (int i = 0; i < numParents; i++)
                    {
                        Parents[i] = (BlueprintId)dataReader.ReadUInt64();
                        byte numOfCopies = dataReader.ReadByte();   // unused
                    }
                }
                else
                {
                    Parents = Array.Empty<BlueprintId>();
                }

                // ContributingBlueprints
                short numContributingBlueprints = dataReader.ReadInt16();
                if (numContributingBlueprints > 0)
                {
                    ContributingBlueprints = new BlueprintId[numContributingBlueprints];
                    for (int i = 0; i < numContributingBlueprints; i++)
                    {
                        ContributingBlueprints[i] = (BlueprintId)dataReader.ReadUInt64();
                        byte numOfCopies = dataReader.ReadByte();   // unused
                    }
                }
                else
                {
                    ContributingBlueprints = Array.Empty<BlueprintId>();
                }

                // Members
                short numMembers = dataReader.ReadInt16();
                _members = new(numMembers);
                for (int i = 0; i < numMembers; i++)
                {
                    StringId fieldId = (StringId)dataReader.ReadUInt64();
                    string fieldName = dataReader.ReadFixedString16();
                    CalligraphyBaseType baseType = (CalligraphyBaseType)dataReader.ReadByte();
                    CalligraphyStructureType structureType = (CalligraphyStructureType)dataReader.ReadByte();

                    if (!Verify.IsTrue(IsSupportedType(baseType, structureType), $"Unsupported field type '{(char)baseType}','{(char)structureType}' for field {fieldName} in blueprint {this}"))
                        return false;

                    if (IsReferenceType(baseType))
                    {
                        ulong subtype = dataReader.ReadUInt64();    // unused
                    }

                    BlueprintMember member = new(fieldId, fieldName, baseType, structureType);
                    _members.Add(member.FieldId, member);
                }
            }
            catch (Exception e)
            {
                Verify.IsTrue(false, e.Message);
                return false;
            }

            // Bind non-property blueprint members to C# reflection metadata
            foreach (BlueprintMember member in _members.Values)
            {
                Type classType = RuntimeBindingClassType;
                while (classType != typeof(Prototype))
                {
                    // Try to find a matching property info in our runtime binding
                    member.RuntimeClassFieldInfo = classType.GetProperty(member.FieldName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
                    if (member.RuntimeClassFieldInfo != null)
                        break;

                    // Go up in the hierarchy if we didn't find it
                    classType = classType.BaseType;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a struct that contains a reference to a <see cref="BlueprintMember"/> and the <see cref="Blueprint"/> it belongs to.
        /// This method searches this blueprint, as well as all of its parents recursively.
        /// </summary>
        public bool GetBlueprintMemberInfo(StringId fieldId, out BlueprintMemberInfo memberInfo)
        {
            // Check if the specified member belongs to this blueprint
            if (_members.TryGetValue(fieldId, out BlueprintMember member))
            {
                memberInfo = new(this, member);
                return true;
            }

            // Check if the specified member belongs to any of our parents
            foreach (BlueprintId parentRef in Parents)
            {
                Blueprint parent = GameDatabase.GetBlueprint(parentRef);
                if (parent.GetBlueprintMemberInfo(fieldId, out memberInfo))
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
        public void PopulateFileIds(HashSet<BlueprintId> callerFileIds)
        {
            // Begin building a new hash set if ours is empty
            if (FileIds.Count == 0)
            {
                FileIds.Add(BlueprintDataRef);     // add this blueprint's id

                // Add parent ids
                foreach (BlueprintId parentRef in Parents)
                {
                    Blueprint parent = GameDatabase.GetBlueprint(parentRef);
                    parent.PopulateFileIds(FileIds);
                }
            }

            // Add this blueprint's hash set if it's a parent of the caller
            if (callerFileIds != FileIds)
            {
                foreach (BlueprintId id in FileIds)
                    callerFileIds.Add(id);
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
        public PrototypeId GetPrototypeFromEnumValue(int enumValue)
        {
            if (!Verify.IsTrue(enumValue < _enumValueToPrototypeLookup.Length)) return PrototypeId.Invalid;
            return _enumValueToPrototypeLookup[enumValue];
        }

        /// <summary>
        /// Gets an enum value for the specified <see cref="PrototypeId"/>. Returns 0 if the prototype does not belong to this blueprint.
        /// </summary>
        public int GetPrototypeEnumValue(PrototypeId prototypeDataRef)
        {
            if (!Verify.IsTrue(_prototypeToEnumValueLookup.TryGetValue(prototypeDataRef, out int enumValue),
                $"Failed to find prototype data ref {prototypeDataRef.GetName()} in enumeration of blueprint {this}.  Perhaps a prototype parameter is being used that conflicts with the blueprint type stored in the property info."))
                return 0;

            return enumValue;
        }

        /// <summary>
        /// Binds this blueprint to a property prototype.
        /// </summary>
        public void SetPropertyPrototypeRef(PrototypeId propertyDataRef)
        {
            Verify.IsTrue(PropertyDataRef == PrototypeId.Invalid || PropertyDataRef == propertyDataRef,
                $"Blueprint {this} cannot be bound to more than one property, already bound to {PropertyDataRef.GetName()} and now trying to bind to {propertyDataRef.GetName()}");
            PropertyDataRef = propertyDataRef;
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
            if (!Verify.IsNotNull(parent)) return false;
            return IsA(parent.BlueprintDataRef);
        }

        /// <summary>
        /// Checks if this blueprint is a child of the provided blueprint in the prototype class hierarchy. Blueprints are also considered children of themselves.
        /// </summary>
        public bool IsRuntimeChildOf(Blueprint parent)
        {
            if (!Verify.IsNotNull(parent)) return false;

            if (parent == this)
                return true;

            return GameDatabase.PrototypeClassManager.PrototypeClassIsA(RuntimeBindingClassType, parent.RuntimeBindingClassType);
        }

        /// <summary>
        /// Searches the blueprint hierarchy for a related blueprint that is bound to the specified class type.
        /// </summary>
        public Blueprint FindRuntimeBindingInBlueprintHierarchy(Type classType, Blueprint parentBlueprint)
        {
            if (RuntimeBindingClassType == classType && IsA(parentBlueprint))
                return this;

            foreach (BlueprintId parentRef in Parents)
            {
                Blueprint parent = GameDatabase.GetBlueprint(parentRef);
                if (!Verify.IsNotNull(parent)) return null;

                Blueprint result = parent.FindRuntimeBindingInBlueprintHierarchy(classType, parentBlueprint);
                if (result != null)
                    return result;
            }

            return null;
        }

        public static bool IsReferenceType(CalligraphyBaseType baseType)
        {
            switch (baseType)
            {
                case CalligraphyBaseType.Asset:
                case CalligraphyBaseType.Curve:
                case CalligraphyBaseType.Prototype:
                case CalligraphyBaseType.RHStruct:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsSupportedType(CalligraphyBaseType baseType, CalligraphyStructureType structureType)
        {
            if (structureType == CalligraphyStructureType.Simple)
                return true;
            
            if (structureType == CalligraphyStructureType.List)
            {
                switch (baseType)
                {
                    case CalligraphyBaseType.Asset:
                    case CalligraphyBaseType.Prototype:
                    case CalligraphyBaseType.RHStruct:
                    case CalligraphyBaseType.Type:
                        return true;
                }
            }

            return false;
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

        public PropertyInfo RuntimeClassFieldInfo { get; set; }     // This is C# reflection property info, not to be confused with entity properties

        public BlueprintMember(StringId fieldId, string fieldName, CalligraphyBaseType baseType, CalligraphyStructureType structureType)
        {
            FieldId = fieldId;
            FieldName = fieldName;
            BaseType = baseType;
            StructureType = structureType;
        }
    }

    /// <summary>
    /// Container for a blueprint member reference along with the blueprint it belongs to.
    /// </summary>
    public readonly struct BlueprintMemberInfo(Blueprint blueprint, BlueprintMember member)
    {
        public Blueprint Blueprint { get; } = blueprint;
        public BlueprintMember Member { get; } = member;
    }
}
