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

        public bool Deserialize(CalligraphyReader dataReader, BlueprintGuid guid, BlueprintId blueprintRef)
        {
            string filename = dataReader.SectionName;

            BlueprintDataRef = blueprintRef;
            Guid = guid;

            if (!Verify.IsTrue(dataReader.ReadHeader("BPT"))) return false;

            // RuntimeBinding
            const int RuntimeBindingMax = 1024;
            Span<byte> runtimeBindingBuffer = stackalloc byte[RuntimeBindingMax];

            if (!Verify.IsTrue(dataReader.ReadStringUTF8(runtimeBindingBuffer, RuntimeBindingMax - 1), $"Unable to read runtime binding in {filename}"))
                return false;

            string runtimeBinding = runtimeBindingBuffer.GetCString();

            Type classType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByName(runtimeBinding);
            if (!Verify.IsNotNull(classType, $"Could not match runtime binding: {runtimeBinding}\nFor Filename: {filename}"))
                return false;

            RuntimeBindingClassType = classType;

            // DefaultPrototypeRef
            if (!Verify.IsTrue(dataReader.Read(out PrototypeId defaultPrototypeId), $"Unable to read default prototype id from {filename}"))
                return false;

            DefaultPrototypeRef = defaultPrototypeId;

            // Parents
            if (!Verify.IsTrue(dataReader.Read(out short numParents), $"Unable to read number of parents for {filename}"))
                return false;

            if (numParents > 0)
            {
                Parents = new BlueprintId[numParents];
                for (int i = 0; i < numParents; i++)
                {
                    if (!Verify.IsTrue(dataReader.Read(out Parents[i]), $"Error reading {i} parent blueprint id from {filename}"))
                        return false;

                    // numOfCopies is unused
                    if (!Verify.IsTrue(dataReader.Read(out byte numOfCopies), $"Error reading {i} parent blueprint num copies from {filename}"))
                        return false;
                }
            }
            else
            {
                Parents = Array.Empty<BlueprintId>();
            }

            // ContributingBlueprints
            if (!Verify.IsTrue(dataReader.Read(out short numContributingBlueprints), $"Unable to read number of contributing blueprints for {filename}"))
                return false;

            if (numContributingBlueprints > 0)
            {
                ContributingBlueprints = new BlueprintId[numContributingBlueprints];
                for (int i = 0; i < numContributingBlueprints; i++)
                {
                    if (!Verify.IsTrue(dataReader.Read(out ContributingBlueprints[i]), $"Error reading {i} contributing blueprint id from {filename}"))
                        return false;

                    // numOfCopies is unused
                    if (!Verify.IsTrue(dataReader.Read(out byte numOfCopies), $"Error reading {i} contributing blueprint num copies from {filename}"))
                        return false;
                }
            }
            else
            {
                ContributingBlueprints = Array.Empty<BlueprintId>();
            }

            // Members
            if (!Verify.IsTrue(dataReader.Read(out short numMembers), $"Unable to read number of members for {filename}"))
                return false;

            _members = new(numMembers);

            const int MaxFieldName = 1024;
            Span<byte> fieldNameBuffer = stackalloc byte[MaxFieldName];

            for (int i = 0; i < numMembers; i++)
            {
                if (!Verify.IsTrue(dataReader.Read(out StringId fieldId), $"Error reading field id #{i} of blueprint {filename}"))
                    return false;

                if (!Verify.IsTrue(dataReader.ReadStringUTF8(fieldNameBuffer, MaxFieldName - 1), $"Error reading field name #{i} in blueprint {filename}"))
                    return false;

                string fieldName = fieldNameBuffer.GetCString();

                if (!Verify.IsTrue(dataReader.Read(out CalligraphyBaseType baseType), $"Error reading base type for field {fieldName} in blueprint {filename}"))
                    return false;

                if (!Verify.IsTrue(dataReader.Read(out CalligraphyStructureType structureType), $"Error reading structure type for field {fieldName} in blueprint {filename}"))
                    return false;

                if (!Verify.IsTrue(IsSupportedType(baseType, structureType), $"Unsupported field type '{(char)baseType}','{(char)structureType}' for field {fieldName} in blueprint {filename}"))
                    return false;

                if (IsReferenceType(baseType))
                {
                    // subtype is unused
                    if (!Verify.IsTrue(dataReader.Read(out ulong subtype), $"Error reading subtype for field {fieldName} in blueprint {filename}"))
                        return false;
                }

                BlueprintMember member = new(fieldId, fieldName, baseType, structureType);
                _members.Add(member.FieldId, member);
            }

            // Bind non-property blueprint members to PrototypeFieldInfo.
            // We don't need an inner loop like the client because our PrototypeFieldSet implementation includes fields from base classes.
            PrototypeClassManager prototypeClassManager = GameDatabase.PrototypeClassManager;
            PrototypeFieldSet fieldSet = prototypeClassManager.GetPrototypeFieldSet(RuntimeBindingClassType);

            foreach (BlueprintMember member in _members.Values)
                member.RuntimeClassFieldInfo = fieldSet.GetFieldInfo(member.FieldName);

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
            if (structureType == CalligraphyStructureType.Single)
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

        public PrototypeFieldInfo RuntimeClassFieldInfo { get; set; }

        public BlueprintMember(StringId fieldId, string fieldName, CalligraphyBaseType baseType, CalligraphyStructureType structureType)
        {
            FieldId = fieldId;
            FieldName = fieldName;
            BaseType = baseType;
            StructureType = structureType;
        }

        public bool IsCompatibleWithType(PrototypeFieldType fieldType)
        {
            switch (fieldType)
            {
                case PrototypeFieldType.Int8:
                case PrototypeFieldType.Int16:
                case PrototypeFieldType.Int32:
                case PrototypeFieldType.Int64:
                case PrototypeFieldType.UInt16:
                case PrototypeFieldType.UInt32:
                case PrototypeFieldType.UInt64:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Long;

                case PrototypeFieldType.Bool:
                    return StructureType == CalligraphyStructureType.Single && (BaseType == CalligraphyBaseType.Long || BaseType == CalligraphyBaseType.Boolean);

                case PrototypeFieldType.Float32:
                case PrototypeFieldType.Float64:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Double;

                case PrototypeFieldType.Enum:
                case PrototypeFieldType.FunctionPtr:
                case PrototypeFieldType.AssetRef:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Asset;

                case PrototypeFieldType.PrototypeDataRef:
                case PrototypeFieldType.PrototypeRefPtr:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Prototype;

                case PrototypeFieldType.AssetTypeRef:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Type;

                case PrototypeFieldType.CurveRef:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.Curve;

                case PrototypeFieldType.LocaleStringId:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.String;

                case PrototypeFieldType.PrototypePtr:
                    return StructureType == CalligraphyStructureType.Single && BaseType == CalligraphyBaseType.RHStruct;

                case PrototypeFieldType.VectorPrototypeDataRef:
                case PrototypeFieldType.ListPrototypeDataRef:
                case PrototypeFieldType.VectorPrototypeRefPtr:
                    return StructureType == CalligraphyStructureType.List && BaseType == CalligraphyBaseType.Prototype;

                case PrototypeFieldType.VectorAssetDataRef:
                case PrototypeFieldType.ListAssetRef:
                case PrototypeFieldType.ListEnum:
                    return StructureType == CalligraphyStructureType.List && BaseType == CalligraphyBaseType.Asset;

                case PrototypeFieldType.ListAssetTypeRef:
                    return StructureType == CalligraphyStructureType.List && BaseType == CalligraphyBaseType.Type;

                case PrototypeFieldType.ListPrototypePtr:
                case PrototypeFieldType.VectorPrototypePtr:
                case PrototypeFieldType.PropertyList:
                    return StructureType == CalligraphyStructureType.List && BaseType == CalligraphyBaseType.RHStruct;

                case PrototypeFieldType.PropertyId:
                    return StructureType == CalligraphyStructureType.Single && (BaseType == CalligraphyBaseType.Prototype || BaseType == CalligraphyBaseType.RHStruct);

                case PrototypeFieldType.Invalid:
                case PrototypeFieldType.Text:
                case PrototypeFieldType.SymbolicBitSet:
                case PrototypeFieldType.Vector3:
                case PrototypeFieldType.Point3:
                case PrototypeFieldType.IPoint3:
                case PrototypeFieldType.Point2:
                case PrototypeFieldType.IPoint2:
                case PrototypeFieldType.Orientation:
                case PrototypeFieldType.Matrix4:
                case PrototypeFieldType.Transform3:
                case PrototypeFieldType.Aabb:
                case PrototypeFieldType.PrototypeGuid:
                case PrototypeFieldType.ResourceName:
                case PrototypeFieldType.Mixin:
                case PrototypeFieldType.Prototype:
                case PrototypeFieldType.ListBool:
                case PrototypeFieldType.ListInt8:
                case PrototypeFieldType.ListInt16:
                case PrototypeFieldType.ListInt32:
                case PrototypeFieldType.ListInt64:
                case PrototypeFieldType.ListFloat32:
                case PrototypeFieldType.ListFloat64:
                case PrototypeFieldType.ListString:
                case PrototypeFieldType.ListMixin:
                case PrototypeFieldType.UnkType52:
                case PrototypeFieldType.Vector:
                case PrototypeFieldType.PropertyCollection:
                    return false;

                default:
                    Verify.IsTrue(false, "Unknown field type in Blueprint::IsCompatibleWithType()");
                    return false;
            }
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
