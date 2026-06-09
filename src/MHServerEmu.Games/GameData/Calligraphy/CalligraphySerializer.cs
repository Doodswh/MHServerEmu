using System.Reflection;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy.Attributes;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    /// <summary>
    /// An implementation of <see cref="GameDataSerializer"/> for Calligraphy prototypes.
    /// </summary>
    public sealed partial class CalligraphySerializer : GameDataSerializer
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public static CalligraphySerializer Instance { get; } = new();

        private CalligraphySerializer() { }

        /// <summary>
        /// Deserializes a Calligraphy prototype from stream.
        /// </summary>
        public override bool Deserialize(Prototype prototype, PrototypeId dataRef, Stream stream)
        {
            string prototypeName = GameDatabase.GetPrototypeName(dataRef);

            using (BinaryReader reader = new(stream))
            {
                // Read Calligraphy header
                CalligraphyHeader calligraphyHeader = new(reader);

                // Read prototype header and check it
                PrototypeDataHeader prototypeHeader = new(reader);
                if (prototypeHeader.ReferenceExists == false) return true;
                if (prototypeHeader.PolymorphicData) return true;

                // Begin deserialization
                DoDeserialize(prototype, prototypeHeader, dataRef, prototypeName, reader);
            }

            return true;
        }

        /// <summary>
        /// Deserializes data for a Calligraphy prototype.
        /// </summary>
        private static bool DoDeserialize(Prototype prototype, PrototypeDataHeader header, PrototypeId prototypeDataRef, string prototypeName, BinaryReader reader)
        {
            DataDirectory dataDirectory = GameDatabase.DataDirectory;

            // Set prototype data ref
            prototype.DataRef = prototypeDataRef;

            // Get blueprint
            Blueprint blueprint = dataDirectory.GetPrototypeBlueprint(prototypeDataRef != PrototypeId.Invalid ? prototypeDataRef : header.ReferenceType);

            // Make sure there is data to deserialize
            if (header.ReferenceExists == false)
                return Logger.ErrorReturn(false, $"DoDeserialize(): Missing reference, file name {prototypeName}");

            // Get class type (we get it from the blueprint's binding instead of calling GetRuntimeClassId())
            Type classType = blueprint.RuntimeBindingClassType;

            // Copy parent data if there is any
            if (header.ReferenceType != PrototypeId.Invalid)
            {
                CopyPrototypeDataRefFields(prototype, header.ReferenceType);
                prototype.ParentDataRef = header.ReferenceType;
            }

            // Deserialize this prototype's data if there is any
            if (header.InstanceDataExists == false) return true;

            short numFieldGroups = reader.ReadInt16();
            for (int i = 0; i < numFieldGroups; i++)
            {
                // Read blueprint information and get the specified blueprint
                BlueprintId groupBlueprintDataRef = (BlueprintId)reader.ReadUInt64();
                byte blueprintCopyNum = reader.ReadByte();
                Blueprint groupBlueprint = GameDatabase.GetBlueprint(groupBlueprintDataRef);

                if (groupBlueprint.IsProperty)
                {
                    if (DeserializePropertyMixin(prototype, blueprint, groupBlueprint, blueprintCopyNum, prototypeDataRef, prototypeName, classType, reader) == false)
                        return Logger.ErrorReturn(false, $"DoDeserialize(): Failed to deserialize property mixin, file name {prototypeName}");
                }
                else
                {
                    // Simple fields
                    if (DeserializeFieldGroup(prototype, blueprint, blueprintCopyNum, prototypeName, classType, reader, "Simple Fields") == false)
                        return Logger.ErrorReturn(false, $"DoDeserialize(): Failed to deserialize simple field group, file name {prototypeName}");

                    // List fields
                    if (DeserializeFieldGroup(prototype, blueprint, blueprintCopyNum, prototypeName, classType, reader, "List Fields") == false)
                        return Logger.ErrorReturn(false, $"DoDeserialize(): Failed to deserialize list field group, file name {prototypeName}");
                }
            }

            return true;
        }

        /// <summary>
        /// Deserializes a field group of a Calligraphy prototype.
        /// </summary>
        private static bool DeserializeFieldGroup(Prototype prototype, Blueprint blueprint, byte blueprintCopyNum, string prototypeName, Type classType, BinaryReader reader, string groupTag)
        {
            var classManager = GameDatabase.PrototypeClassManager;

            short numFields = reader.ReadInt16();
            for (int i = 0; i < numFields; i++)
            {
                var fieldId = (StringId)reader.ReadUInt64();
                var fieldBaseType = (CalligraphyBaseType)reader.ReadByte();

                // Get blueprint member info for this field
                if (blueprint.GetBlueprintMemberInfo(fieldId, out var blueprintMemberInfo) == false)
                    return Logger.ErrorReturn(false, $"DeserializeFieldGroup(): Failed to find member id {fieldId} in blueprint {GameDatabase.GetBlueprintName(blueprint.BlueprintDataRef)}");

                // Check to make sure the type matches (do we need this?)
                if (blueprintMemberInfo.Member.BaseType != fieldBaseType)
                    return Logger.ErrorReturn(false, $"DeserializeFieldGroup(): Type mismatch between blueprint and prototype");

                // Determine where this field belongs
                Prototype fieldOwnerPrototype = prototype;
                Blueprint fieldOwnerBlueprint = blueprint;

                System.Reflection.PropertyInfo fieldInfo;
                if (blueprint.IsRuntimeChildOf(blueprintMemberInfo.Blueprint))
                {
                    // For regular fields we just get field info straight away
                    fieldInfo = classManager.GetFieldInfo(blueprint.RuntimeBindingClassType, blueprintMemberInfo, false);
                }
                else
                {
                    // The blueprint for this field is not a runtime child of our main blueprint, meaning it belongs to one of the mixins
                    fieldOwnerBlueprint = blueprintMemberInfo.Blueprint;
                    Type mixinType = blueprintMemberInfo.Blueprint.RuntimeBindingClassType;

                    // Currently known cases for non-property mixins:
                    // - LocomotorPrototype and PopulationInfoPrototype in AgentPrototype (simple mixins, PopulationInfoPrototype seems to be unused)
                    // - ProductPrototype in ItemPrototype (simple mixin)
                    // - ConditionPrototype and ConditionEffectPrototype in PowerPrototype (list mixins)
                    // We use MixinAttribute and ListMixinAttribute to differentiate them from RHStructs.

                    // First we look for a non-list mixin field
                    var mixinFieldInfo = classManager.GetMixinFieldInfo(classType, mixinType, PrototypeFieldType.Mixin);
                    if (mixinFieldInfo != null)
                    {
                        // Set owner prototype to the existing mixin instance or create a new instance if there isn't one
                        fieldOwnerPrototype = (Prototype)mixinFieldInfo.GetValue(prototype);
                        if (fieldOwnerPrototype == null)
                        {
                            fieldOwnerPrototype = GameDatabase.PrototypeClassManager.AllocatePrototype(mixinType);
                            mixinFieldInfo.SetValue(prototype, fieldOwnerPrototype);
                        }

                        // Get the field info from our mixin
                        fieldInfo = classManager.GetFieldInfo(mixinType, blueprintMemberInfo, false);
                    }
                    else
                    {
                        // Look for a list mixin
                        mixinFieldInfo = classManager.GetMixinFieldInfo(classType, mixinType, PrototypeFieldType.ListMixin);
                        if (mixinFieldInfo != null)
                        {
                            PrototypeMixinList list = AcquireOwnedMixinList(prototype, mixinFieldInfo, false);

                            // Get a matching list element
                            Prototype element = AcquireOwnedUniqueMixinListElement(prototype, list, mixinType, fieldOwnerBlueprint, blueprintCopyNum);
                            if (element == null)
                                return Logger.ErrorReturn(false, $"DeserializeFieldGroup(): Failed to acquire element of a list mixin to deserialize field into");

                            fieldOwnerPrototype = element;
                            fieldInfo = classManager.GetFieldInfo(mixinType, blueprintMemberInfo, false);
                        }
                        else
                        {
                            // Nowhere to put this field, something went very wrong, time to reevaluate life choices
                            return Logger.ErrorReturn(false, $"DeserializeFieldGroup(): Failed to find field info for mixin {mixinType.Name}, field name {blueprintMemberInfo.Member.FieldName}");
                        }
                    }
                }

                // Parse
                ParseDelegate parse = GetParser(classManager.GetPrototypeFieldTypeEnumValue(fieldInfo));
                FieldParserParams @params = new(reader, fieldInfo, fieldOwnerPrototype, fieldOwnerBlueprint, prototypeName, blueprintMemberInfo);
                
                if (parse(@params) == false)
                {
                    return Logger.ErrorReturn(false, string.Format("DeserializeFieldGroup(): Failed to parse field {0} of field group {1}, file name {2}",
                        blueprintMemberInfo.Member.FieldName,
                        GameDatabase.GetBlueprintName(blueprint.BlueprintDataRef),
                        prototypeName));
                };
            }

            return true;
        }

        #region Properties

        /// <summary>
        /// Deserializes a property mixin field group of a Calligraphy prototype.
        /// </summary>
        private static bool DeserializePropertyMixin(Prototype prototype, Blueprint blueprint, Blueprint groupBlueprint, byte blueprintCopyNum,
            PrototypeId prototypeDataRef, string prototypeFilePath, Type classType, BinaryReader reader)
        {
            // This whole mixin system is a huge mess.
            PrototypePropertyCollection collection = null;

            // Property mixins are used both for initializing property infos and filling prototype property collections.
            // If this isn't a default prototype, it means the field group needs to be deserialized into a property collection.
            if (prototypeDataRef != groupBlueprint.DefaultPrototypeRef)
            {
                Type propertyHolderClassType = classType;
                Prototype propertyHolderPrototype = prototype;

                // Check if this property belongs in one of mixin property collections.
                // This is basically an edge case for PowerPrototype, since it's the only example of mixins having their own property collections.
                // We save a little bit of time by skipping this for non-power prototypes. Remove this check if something breaks in other versions of the game.
                if (prototype is PowerPrototype)
                {
                    // Iterate through all fields and check if if there are any mixin fields.
                    // NOTE: the client uses a nested loop here and iterates through all parents until it founds a mixin or reaches the top of the hierarchy.
                    // Since C# reflection already contains all inherited properties we can do it in a single foreach loop.
                    bool foundMixin = false;
                    foreach (var fieldInfo in classType.GetProperties())
                    {
                        PrototypeFieldType fieldType = GameDatabase.PrototypeClassManager.GetPrototypeFieldTypeEnumValue(fieldInfo);

                        // If this is a mixin check if it has property collections we need to deserialize into
                        // We pass propertyHolderClassType and propertyHolderPrototype as refs so that CheckPropertyMixin can modify them
                        if (fieldType == PrototypeFieldType.Mixin || fieldType == PrototypeFieldType.ListMixin)
                            foundMixin = CheckPropertyMixin(fieldInfo, fieldType, blueprint, groupBlueprint, blueprintCopyNum, prototype,
                                ref propertyHolderClassType, ref propertyHolderPrototype);

                        if (foundMixin) break;
                    }
                }
                
                // Get property collection to deserialize into from the property holder
                // Note: going through the PrototypeClassManager to get property collection field info doesn't make a whole lot of sense
                // in the context of our implementation, but that's how it's done in the client, so it's going to be this way (at least for now).
                var collectionFieldInfo = GameDatabase.PrototypeClassManager.GetFieldInfo(propertyHolderClassType, null, true);
                if (collectionFieldInfo == null)
                    return Logger.WarnReturn(false, "DeserializePropertyMixin(): Failed to get property collection field info for property mixin");
                collection = GetPropertyCollectionField(propertyHolderPrototype, collectionFieldInfo);
            }

            // This handles both cases (initialization and filling property collections)
            DeserializeFieldGroupIntoProperty(collection, groupBlueprint, blueprintCopyNum, prototypeFilePath, reader, "Property Fields");

            // Property field groups do not have any list fields, so numListFields should always be 0
            short numListFields = reader.ReadInt16();
            if (numListFields != 0)
                return Logger.WarnReturn(false, $"DeserializePropertyMixin(): Property field group numListFields != 0");
            return true;
        }

        /// <summary>
        /// Deserializes a property field group.
        /// </summary>
        private static bool DeserializeFieldGroupIntoProperty(PrototypePropertyCollection propertyCollection, Blueprint groupBlueprint, byte blueprintCopyNum,
            string prototypeName, BinaryReader reader, string groupTag)
        {
            if (groupBlueprint.IsProperty == false) return false;

            PropertyInfoTable propertyInfoTable = GameDatabase.PropertyInfoTable;

            PrototypeId propertyDataRef = groupBlueprint.PropertyDataRef;
            PropertyEnum propertyEnum = propertyInfoTable.GetPropertyEnumFromPrototype(propertyDataRef);
            bool isInitializing = propertyCollection == null;

            PropertyBuilder propertyBuilder = new(propertyEnum, propertyInfoTable, isInitializing);
            if (DeserializeFieldGroupIntoPropertyBuilder(ref propertyBuilder, groupBlueprint, prototypeName, reader, isInitializing, groupTag) == false)
                return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoProperty(): Failed to deserialize field group into property builder");

            if (isInitializing)
            {
                propertyBuilder.SetPropertyInfo();
                return true;
            }

            // We should get here only after we have already initialized all property infos
            var info = GameDatabase.PropertyInfoTable.LookupPropertyInfo(propertyEnum);

            // Build property id
            PropertyId propertyId = propertyBuilder.GetPropertyId();

            // Set a property or override the id of an existing one
            if (info.IsCurveProperty == false)
            {
                if (propertyBuilder.IsValueSet)
                {
                    // Set a property if we have a value
                    propertyCollection.SetPropertyFromMixin(propertyBuilder.PropertyValue, propertyId, blueprintCopyNum, propertyBuilder.ParamsSetMask);
                }
                else
                {
                    // If no value is defined in the field group it means we need to override the id (params) of an existing value
                    propertyCollection.ReplacePropertyIdFromMixin(propertyId, blueprintCopyNum, propertyBuilder.ParamsSetMask);
                }
            }
            else
            {
                // Set a curve property if we have a value
                if (propertyBuilder.IsValueSet)
                {
                    // Get curve id and index property from the builder
                    CurveId curveId = propertyBuilder.PropertyValue;
                    if (curveId == CurveId.Invalid) Logger.WarnReturn(false, "DeserializeFieldGroupIntoProperty(): curveId is invalid");
                    PropertyId indexProperty = propertyBuilder.CurveIndex;

                    // Add a new curve property to the collection
                    propertyCollection.SetCurvePropertyFromMixin(propertyId, curveId, indexProperty, info, blueprintCopyNum);
                }
                else
                {
                    // Override property id and / or curve index property of an existing property
                    if (propertyBuilder.IsCurveIndexSet)
                    {
                        // Override both the property id and the index property
                        PropertyId indexProperty = propertyBuilder.CurveIndex;
                        if (indexProperty.Enum == PropertyEnum.Invalid)
                            Logger.WarnReturn(false, "DeserializeFieldGroupIntoProperty(): Curve properties must have an index property");
                        propertyCollection.ReplaceCurvePropertyIdFromMixin(propertyId, indexProperty, info, blueprintCopyNum, propertyBuilder.ParamsSetMask);
                    }
                    else
                    {
                        // Override just the id of the property itself if no curve index is provided in the field group
                        propertyCollection.ReplaceCurvePropertyIdFromMixin(propertyId, info, blueprintCopyNum, propertyBuilder.ParamsSetMask);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Deserializes a property field group into a <see cref="PropertyBuilder"/> instance.
        /// </summary>
        private static bool DeserializeFieldGroupIntoPropertyBuilder(ref PropertyBuilder builder, Blueprint blueprint, string prototypeName, BinaryReader reader, bool isInitializing, string groupTag)
        {
            PrototypeId propertyDataRef = blueprint.PropertyDataRef;
            PropertyEnum propertyEnum = GameDatabase.PropertyInfoTable.GetPropertyEnumFromPrototype(propertyDataRef);

            if (propertyEnum == PropertyEnum.Invalid)
                return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to get property enum value, file name {prototypeName}");

            short numFields = reader.ReadInt16();

            if (numFields <= 0)
                return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): <= 0 fields in a property field group, file name {prototypeName}");

            for (int i = 0; i < numFields; i++)
            {
                var fieldId = (StringId)reader.ReadUInt64();
                var type = (CalligraphyBaseType)reader.ReadByte();

                if (fieldId == StringId.Invalid)
                    return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Invalid field id in a property field group, file name {prototypeName}");

                if (blueprint.GetBlueprintMemberInfo(fieldId, out var blueprintMemberInfo) == false)
                    return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to get blueprint member info for field id {fieldId}, file name {prototypeName}");

                // Fields with the same name can have different field ids in different property prototypes
                // (most likely due to how they are hashed), so we have no choice but to work with strings here.
                string fieldName = blueprintMemberInfo.Member.FieldName;

                if (string.Equals(fieldName, "Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (DeserializePropertyValue(blueprint, prototypeName, reader, blueprintMemberInfo, out PropertyValue value) == false)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to deserialize property value field, file name {prototypeName}");

                    if (builder.SetValue(value) == false)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to set property value field, file name {prototypeName}");
                }
                else if (string.Equals(fieldName, "CurveIndex", StringComparison.OrdinalIgnoreCase))
                {
                    if (DeserializePropertyValue(blueprint, prototypeName, reader, blueprintMemberInfo, out PropertyValue curveIndex) == false)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to deserialize property curve index field, file name {prototypeName}");

                    if (builder.SetCurveIndex((PrototypeId)curveIndex) == false)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to set property curve index field, file name {prototypeName}");
                }
                else if (fieldName.StartsWith("Param", StringComparison.OrdinalIgnoreCase))
                {
                    int paramIndex;
                    if (fieldName.Length < 6)
                    {
                        Logger.Trace($"DeserializeFieldGroupIntoPropertyBuilder(): Param field name '{fieldName}' does not contain param index, defaulting to 0, file name {prototypeName}");
                        paramIndex = 0;
                    }
                    else
                    {
                        paramIndex = int.Parse(fieldName[5].ToString());
                    }

                    if (paramIndex >= Property.MaxParamCount)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Property param index {paramIndex} out of range");

                    if (DeserializePropertyParam(blueprintMemberInfo, prototypeName, reader, paramIndex, ref builder) == false)
                        return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Failed to deserialize property param field, file name {prototypeName}");
                }
                else
                {
                    return Logger.ErrorReturn(false, $"DeserializeFieldGroupIntoPropertyBuilder(): Unexpected field name '{fieldName}' in a property field group, file name {prototypeName}");
                }
            }

            return true;
        }

        /// <summary>
        /// Deserializes a <see cref="PropertyValue"/> from a field group.
        /// </summary>
        private static bool DeserializePropertyValue(Blueprint blueprint, string prototypeName, BinaryReader reader, BlueprintMemberInfo blueprintMemberInfo, out PropertyValue value)
        {
            switch (blueprintMemberInfo.Member.BaseType)
            {
                case CalligraphyBaseType.Asset:     value = (AssetId)reader.ReadUInt64(); break;
                case CalligraphyBaseType.Boolean:   value = Convert.ToBoolean(reader.ReadUInt64()); break;
                case CalligraphyBaseType.Curve:     value = (CurveId)reader.ReadUInt64(); break;
                case CalligraphyBaseType.Double:    value = (float)reader.ReadDouble(); break;
                case CalligraphyBaseType.Long:      value = reader.ReadInt64(); break;
                case CalligraphyBaseType.Prototype: value = (PrototypeId)reader.ReadUInt64(); break;

                default: value = new(); return Logger.WarnReturn(false, "DeserializePropertyValue(): Unhandled base type for property value");
            }

            return true;
        }

        /// <summary>
        /// Deserializes a <see cref="PropertyParam"/> from a field group and sets it in the provided <see cref="PropertyBuilder"/> instance.
        /// </summary>
        private static bool DeserializePropertyParam(BlueprintMemberInfo blueprintMemberInfo, string prototypeName, BinaryReader reader, int paramIndex, ref PropertyBuilder builder)
        {
            if (blueprintMemberInfo.Member.StructureType == CalligraphyStructureType.List)
                return Logger.ErrorReturn(false, $"DeserializePropertyParam(): Unhandled structure type for property param");

            switch (blueprintMemberInfo.Member.BaseType)
            {
                case CalligraphyBaseType.Long:
                    long integerParam = reader.ReadInt64();
                    if (builder.SetIntegerParam(paramIndex, integerParam) == false)
                        return Logger.ErrorReturn(false, $"DeserializePropertyParam(): Failed to set property integer param, file name {prototypeName}");
                    break;

                case CalligraphyBaseType.Asset:
                    var assetRef = (AssetId)reader.ReadUInt64();
                    if (builder.SetAssetParam(paramIndex, assetRef) == false)
                        return Logger.ErrorReturn(false, $"DeserializePropertyParam(): Failed to set property asset param, file name {prototypeName}");
                    break;

                case CalligraphyBaseType.Prototype:
                    var field = (PrototypeId)reader.ReadUInt64();
                    if (builder.SetPrototypeParam(paramIndex, field) == false)
                        return Logger.ErrorReturn(false, $"DeserializePropertyParam(): Failed to set property prototype param, file name {prototypeName}");
                    break;

                default:
                    return Logger.ErrorReturn(false, "DeserializePropertyParam(): Unhandled base type for property param");
            }

            return true;
        }

        /// <summary>
        /// Deserializes a standalone <see cref="PropertyId"/> from a field group.
        /// </summary>
        private static bool DeserializeFieldGroupIntoPropertyId(ref PropertyId propertyId, Blueprint blueprint, string prototypeName, BinaryReader reader, string groupTag)
        {
            PropertyInfoTable propertyInfoTable = GameDatabase.PropertyInfoTable;

            PrototypeId propertyPrototypeRef = blueprint.PropertyDataRef;
            PropertyEnum propertyEnum = propertyInfoTable.GetPropertyEnumFromPrototype(propertyPrototypeRef);
            if (propertyEnum == PropertyEnum.Invalid)
                return Logger.ErrorReturn(false, "DeserializeFieldGroupIntoPropertyId(): Invalid property enum");

            PropertyBuilder propertyBuilder = new(propertyEnum, propertyInfoTable, false);
            if (DeserializeFieldGroupIntoPropertyBuilder(ref propertyBuilder, blueprint, prototypeName, reader, false, groupTag) == false)
                return Logger.ErrorReturn(false, "DeserializeFieldGroupIntoPropertyId(): Failed to deserialize field group into property builder");

            propertyId = propertyBuilder.GetPropertyId();
            if (propertyId == PropertyId.Invalid)
                return Logger.ErrorReturn(false, "DeserializeFieldGroupIntoPropertyId(): PropertyId is invalid");

            return true;
        }

        /// <summary>
        /// Returns the <see cref="PrototypePropertyCollection"/> belonging to the provided <see cref="Prototype"/> if it has one.
        /// Returns <see langword="null"/> if the prototype has no <see cref="PrototypePropertyCollection"/> fields.
        /// </summary>
        public static PrototypePropertyCollection GetPropertyCollectionField(Prototype prototype)
        {
            // NOTE: This method is public because it is also used by PowerPrototype during post-processing.
            // Maybe we should move this to PrototypeClassManager where it makes more sense to be.

            // In all of our data there is never more than one PrototypePropertyCollection field,
            // and it's always called Properties, so we will make use of that to avoid iterating through
            // all fields.
            var fieldInfo = prototype.GetType().GetProperty("Properties", typeof(PrototypePropertyCollection));
            if (fieldInfo != null) return GetPropertyCollectionField(prototype, fieldInfo);
            return null;
        }

        /// <summary>
        /// Returns the <see cref="PrototypePropertyCollection"/> belonging to the provided <see cref="Prototype"/>.
        /// </summary>
        private static PrototypePropertyCollection GetPropertyCollectionField(Prototype prototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var collection = (PrototypePropertyCollection)fieldInfo.GetValue(prototype);

            // Initialize a new collection in this field if there isn't one already or it doesn't belong to it
            if (collection == null || prototype.IsDynamicFieldOwnedBy(collection) == false)
            {
                // Copy parent collection if there is one, otherwise start with a blank one
                collection = collection == null ? new() : collection.ShallowCopy();
                fieldInfo.SetValue(prototype, collection);
                prototype.SetDynamicFieldOwner(collection);
            }

            return collection;
        }

        /// <summary>
        /// Checks if a mixin field should hold the specified property in its collection.
        /// </summary>
        private static bool CheckPropertyMixin(System.Reflection.PropertyInfo mixinFieldInfo, PrototypeFieldType fieldType, Blueprint prototypeBlueprint,
            Blueprint propertyBlueprint, byte blueprintCopyNum, Prototype parentPrototype, ref Type propertyHolderClassType, ref Prototype propertyHolderPrototype)
        {
            Type bindingType = fieldType == PrototypeFieldType.ListMixin
                ? mixinFieldInfo.GetCustomAttribute<ListMixinAttribute>().FieldType
                : mixinFieldInfo.PropertyType;

            Blueprint mixinBlueprint = prototypeBlueprint.FindRuntimeBindingInBlueprintHierarchy(bindingType, propertyBlueprint);
            if (mixinBlueprint == null) return false;

            propertyHolderClassType = mixinBlueprint.RuntimeBindingClassType;
            propertyHolderPrototype = AcquireMixinElement(parentPrototype, propertyHolderClassType, prototypeBlueprint,
                mixinBlueprint, blueprintCopyNum, mixinFieldInfo, fieldType);
            return true;
        }

        #endregion

        #region Field Copying

        /// <summary>
        /// Copies field values from a <see cref="Prototype"/> with the specified data ref.
        /// </summary>
        public static bool CopyPrototypeDataRefFields(Prototype destPrototype, PrototypeId sourceDataRef)
        {
            // Check to make sure our reference is valid
            if (sourceDataRef == PrototypeId.Invalid)
                return Logger.ErrorReturn(false, "CopyPrototypeDataRefFields(): invalid source ref");

            // Get source prototype and copy fields from it
            Prototype sourcePrototype = GameDatabase.GetPrototype<Prototype>(sourceDataRef);
            return CopyPrototypeFields(destPrototype, sourcePrototype);
        }

        /// <summary>
        /// Copies all appropriate field values from one <see cref="Prototype"/> instance to another.
        /// </summary>
        private static bool CopyPrototypeFields(Prototype destPrototype, Prototype sourcePrototype)
        {
            // In some cases (e.g. PopulationInfoPrototype mixin) destination and/or source may be null
            if (destPrototype == null || sourcePrototype == null) return false;

            // Get type information for both prototypes and make sure they are the same
            Type destType = destPrototype.GetType();
            Type sourceType = sourcePrototype.GetType();

            if (sourceType != destType)
                return Logger.ErrorReturn(false, $"CopyPrototypeFields(): source type ({sourceType.Name}) does not match destination type ({destType.Name})");

            foreach (PrototypeClassManager.CachedPrototypeField copyableField in GameDatabase.PrototypeClassManager.GetCopyablePrototypeFields(destType))
            {
                System.Reflection.PropertyInfo fieldInfo = copyableField.FieldInfo;

                switch (copyableField.FieldType)
                {
                    case PrototypeFieldType.Bool:
                    case PrototypeFieldType.Int8:
                    case PrototypeFieldType.Int16:
                    case PrototypeFieldType.Int32:
                    case PrototypeFieldType.Int64:
                    case PrototypeFieldType.Float32:
                    case PrototypeFieldType.Float64:
                    case PrototypeFieldType.Enum:
                    case PrototypeFieldType.AssetRef:
                    case PrototypeFieldType.AssetTypeRef:
                    case PrototypeFieldType.CurveRef:
                    case PrototypeFieldType.PrototypeDataRef:
                    case PrototypeFieldType.LocaleStringId:
                    case PrototypeFieldType.PrototypePtr:
                    case PrototypeFieldType.PropertyId:
                        AssignPointedAtValues(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.ListBool:
                    case PrototypeFieldType.ListInt8:
                    case PrototypeFieldType.ListInt16:
                    case PrototypeFieldType.ListInt32:
                    case PrototypeFieldType.ListInt64:
                    case PrototypeFieldType.ListFloat32:
                    case PrototypeFieldType.ListFloat64:
                    case PrototypeFieldType.ListEnum:
                    case PrototypeFieldType.ListAssetRef:
                    case PrototypeFieldType.ListAssetTypeRef:
                    case PrototypeFieldType.ListPrototypeDataRef:
                    case PrototypeFieldType.ListPrototypePtr:
                        ShallowCopyCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.Mixin:
                        CopyMixin(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.ListMixin:
                        CopyMixinCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.PropertyList:
                    case PrototypeFieldType.PropertyCollection:
                        CopyPrototypePropertyCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    default:
                        Logger.Warn($"CopyPrototypeFields(): Trying to copy unhandled prototype field type {fieldInfo.PropertyType.Name}");
                        continue;
                }
            }

            return true;
        }

        /// <summary>
        /// Copies a field value from one <see cref="Prototype"/> instance to another.
        /// </summary>
        private static void AssignPointedAtValues(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            //fieldInfo.SetValue(destPrototype, fieldInfo.GetValue(sourcePrototype));
            fieldInfo.CopyValue(sourcePrototype, destPrototype);
        }

        /// <summary>
        /// Shallow copies a collection field from a source <see cref="Prototype"/>.
        /// </summary>
        private static void ShallowCopyCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var sourceData = (Array)fieldInfo.GetValue(sourcePrototype);
            if (sourceData == null) return;

            int numItems = sourceData.Length;
            var destData = Array.CreateInstance(fieldInfo.PropertyType.GetElementType(), numItems);
            Array.Copy(sourceData, destData, numItems);
            fieldInfo.SetValue(destPrototype, destData);
        }

        /// <summary>
        /// Copies a mixin field from a source <see cref="Prototype"/>.
        /// </summary>
        private static void CopyMixin(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var sourceMixin = (Prototype)fieldInfo.GetValue(sourcePrototype);
            if (sourceMixin == null) return;

            // Create the mixin instance on the destination prototype if there is something to copy and copy data to it
            var destMixin = GameDatabase.PrototypeClassManager.AllocatePrototype(fieldInfo.PropertyType);
            fieldInfo.SetValue(destPrototype, destMixin);

            CopyPrototypeFields(destMixin, sourceMixin);
        }

        /// <summary>
        /// Copies a <see cref="PrototypeMixinList"/> from a source <see cref="Prototype"/>.
        /// </summary>
        private static void CopyMixinCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var sourceList = (PrototypeMixinList)fieldInfo.GetValue(sourcePrototype);
            if (sourceList == null) return;

            // Create a new list mixin on the destination prototype and take ownership of it
            PrototypeMixinList destList = new();
            fieldInfo.SetValue(destPrototype, destList);
            destPrototype.SetDynamicFieldOwner(destList);

            // Copy all items from the old list
            foreach (var sourceListItem in sourceList)
            {
                // Create a new item in the destination list
                PrototypeMixinListItem destListItem = new();
                destList.Add(destListItem);

                // Create a copy of the mixin from the source list and take ownership of it
                destListItem.Prototype = AllocateDynamicPrototype(sourceListItem.Prototype.GetType(), PrototypeId.Invalid, sourceListItem.Prototype);
                destListItem.Prototype.ParentDataRef = sourceListItem.Prototype.ParentDataRef;
                destPrototype.SetDynamicFieldOwner(destListItem.Prototype);

                // Copy list item metadata
                destListItem.BlueprintId = sourceListItem.BlueprintId;
                destListItem.BlueprintCopyNum = sourceListItem.BlueprintCopyNum;
            }
        }

        /// <summary>
        /// Copies a <see cref="PrototypePropertyCollection"/> from a source <see cref="Prototype"/>.
        /// </summary>
        private static void CopyPrototypePropertyCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var sourcePropertyCollection = (PrototypePropertyCollection)fieldInfo.GetValue(sourcePrototype);
            if (sourcePropertyCollection == null) return;

            // Create a copy of the source property collection and take ownership of it
            var destPropertyCollection = sourcePropertyCollection.ShallowCopy();
            fieldInfo.SetValue(destPrototype, destPropertyCollection);
            destPrototype.SetDynamicFieldOwner(destPropertyCollection);
        }

        #endregion

        #region Mixin Management

        /// <summary>
        /// Acquires either the mixin itself or an element from a list mixin.
        /// </summary>
        private static Prototype AcquireMixinElement(Prototype ownerPrototype, Type elementClassType, Blueprint ownerBlueprint, Blueprint mixinBlueprint,
            byte blueprintCopyNum, System.Reflection.PropertyInfo mixinFieldInfo, PrototypeFieldType fieldType)
        {
            if (fieldType == PrototypeFieldType.Mixin)
            {
                // Allocate a simple mixin if needed and return it
                var element = (Prototype)mixinFieldInfo.GetValue(ownerPrototype);
                if (element == null)
                {
                    element = GameDatabase.PrototypeClassManager.AllocatePrototype(mixinFieldInfo.PropertyType);
                    mixinFieldInfo.SetValue(ownerPrototype, element);
                }

                return element;
            }
            else if (fieldType == PrototypeFieldType.ListMixin)
            {
                PrototypeMixinList list = AcquireOwnedMixinList(ownerPrototype, mixinFieldInfo, false);
                if (list == null) Logger.ErrorReturn<Prototype>(null, "AcquireMixinElement(): failed to acquire mixin list");
                return AcquireOwnedUniqueMixinListElement(ownerPrototype, list, elementClassType, mixinBlueprint, blueprintCopyNum);
            }

            return Logger.ErrorReturn<Prototype>(null, $"AcquireMixinElement(): {fieldType} is not a mixin");
        }

        /// <summary>
        /// Creates if needed and returns a <see cref="PrototypeMixinList"/> from the specified field of the provided <see cref="Prototype"/> instance that belongs to it.
        /// </summary>
        public static PrototypeMixinList AcquireOwnedMixinList(Prototype prototype, System.Reflection.PropertyInfo mixinFieldInfo, bool copyItemsFromParent)
        {
            // Make sure the field info we have is for a list mixin
            if (mixinFieldInfo.PropertyType != typeof(PrototypeMixinList))
                return Logger.ErrorReturn<PrototypeMixinList>(null, $"AcquireOwnedMixinList(): Tried to acquire owned mixin list for a field that is not a list mixin");

            // Create a new list if there isn't one or it belongs to another prototype
            var list = (PrototypeMixinList)mixinFieldInfo.GetValue(prototype);
            if (list == null || prototype.IsDynamicFieldOwnedBy(list) == false)
            {
                PrototypeMixinList newList = new();

                // Fill the new list
                if (list != null)
                {
                    if (copyItemsFromParent)
                    {
                        // Create copies of all parent items and take ownership of those copies
                        foreach (var item in list)
                            AddMixinListItemCopy(prototype, newList, item);
                    }
                    else
                    {
                        // Do a shallow copy of the parent list and do not take ownership of any of its items
                        // In this case copies are created when each list element is acquired with AcquireOwnedUniqueMixinListElement()
                        newList.AddRange(list);
                    }
                }

                // Assign the new list to the field and take ownership of it
                mixinFieldInfo.SetValue(prototype, newList);
                prototype.SetDynamicFieldOwner(newList);

                list = newList;
            }

            return list;
        }

        /// <summary>
        /// Creates if needed and returns a <see cref="Prototype"/> element from a <see cref="PrototypeMixinList"/>.
        /// </summary>
        private static Prototype AcquireOwnedUniqueMixinListElement(Prototype owner, PrototypeMixinList list, Type elementClassType,
            Blueprint elementBlueprint, byte blueprintCopyNum)
        {
            // Look for a unique list element
            // Instead of calling a separate findUniqueMixinListElement() method like the client does, we'll just look for it here
            PrototypeMixinListItem uniqueListElement = null;
            foreach (var element in list)
            {
                // Type check goes last because it's the most expensive one
                if (element.BlueprintId == elementBlueprint.BlueprintDataRef && element.BlueprintCopyNum == blueprintCopyNum && element.Prototype.GetType() == elementClassType)
                {
                    uniqueListElement = element;
                    break;
                }
            }

            if (uniqueListElement == null)
            {
                // Create the element we're looking for if it's not in our list
                Prototype prototype = AllocateDynamicPrototype(elementClassType, elementBlueprint.DefaultPrototypeRef, null);
                prototype.ParentDataRef = elementBlueprint.DefaultPrototypeRef;

                // Assign ownership of the new mixin
                owner.SetDynamicFieldOwner(prototype);

                // Add the new mixin to the list
                PrototypeMixinListItem newListItem = new()
                {
                    Prototype = prototype,
                    BlueprintId = elementBlueprint.BlueprintDataRef,
                    BlueprintCopyNum = blueprintCopyNum
                };

                list.Add(newListItem);

                // Return the new mixin
                return prototype;
            }
            else
            {
                // Return the item we found

                // Return the prototype as is if it belongs to our owner
                if (owner.IsDynamicFieldOwnedBy(uniqueListElement.Prototype))
                    return uniqueListElement.Prototype;

                // If there is a matching item but it doesn't belong to the owner, then we need to replace it with a copy
                list.Remove(uniqueListElement);
                return AddMixinListItemCopy(owner, list, uniqueListElement);
            }
        }

        /// <summary>
        /// Creates a copy of a <see cref="Prototype"/> element from a parent <see cref="PrototypeMixinList"/> and assigns it to the child.
        /// </summary>
        private static Prototype AddMixinListItemCopy(Prototype owner, PrototypeMixinList list, PrototypeMixinListItem item)
        {
            // Copy the prototype from the provided list item
            Prototype element = AllocateDynamicPrototype(item.Prototype.GetType(), PrototypeId.Invalid, item.Prototype);

            // Update parent
            element.ParentDataRef = item.Prototype.DataRef;

            // Update ownership
            owner.SetDynamicFieldOwner(element);

            // Add the copied item to the list
            item.Prototype = element;
            list.Add(item);

            return element;
        }

        /// <summary>
        /// Creates a new <see cref="Prototype"/> of the specified <see cref="Type"/> and fills it with data from the specified source (either a default prototype or a prototype instance).
        /// </summary>
        private static Prototype AllocateDynamicPrototype(Type classType, PrototypeId defaults, Prototype instanceToCopy)
        {
            // Create a new prototype of the specified type
            var prototype = GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            // Copy fields either from the specified defaults prototype or the provided prototype
            if (defaults != PrototypeId.Invalid && instanceToCopy == null)
            {
                var defaultsProto = GameDatabase.GetPrototype<Prototype>(defaults);
                CopyPrototypeFields(prototype, defaultsProto);
            }
            else if (instanceToCopy != null)
            {
                CopyPrototypeFields(prototype, instanceToCopy);
            }

            return prototype;
        }

        #endregion

        #region Parsing

        private delegate bool ParseDelegate(in FieldParserParams @params);

        private static readonly Dictionary<PrototypeFieldType, ParseDelegate> ParseDelegateDict = new()
        {
            { PrototypeFieldType.Bool,                  ParseBool },
            { PrototypeFieldType.Int8,                  ParseInt8 },
            { PrototypeFieldType.Int16,                 ParseInt16 },
            { PrototypeFieldType.Int32,                 ParseInt32 },
            { PrototypeFieldType.Int64,                 ParseInt64 },
            { PrototypeFieldType.Float32,               ParseFloat32 },
            { PrototypeFieldType.Float64,               ParseFloat64 },
            { PrototypeFieldType.Enum,                  ParseEnum },
            { PrototypeFieldType.AssetRef,              ParseDataRef },
            { PrototypeFieldType.AssetTypeRef,          ParseDataRef },
            { PrototypeFieldType.CurveRef,              ParseDataRef },
            { PrototypeFieldType.PrototypeDataRef,      ParseDataRef },
            { PrototypeFieldType.LocaleStringId,        ParseDataRef },
            { PrototypeFieldType.PrototypePtr,          ParsePrototypePtr },
            { PrototypeFieldType.PropertyId,            ParsePropertyId },
            { PrototypeFieldType.ListBool,              ParseListBool },
            { PrototypeFieldType.ListInt8,              ParseListInt8 },
            { PrototypeFieldType.ListInt16,             ParseListInt16 },
            { PrototypeFieldType.ListInt32,             ParseListInt32 },
            { PrototypeFieldType.ListInt64,             ParseListInt64 },
            { PrototypeFieldType.ListFloat32,           ParseListFloat32 },
            { PrototypeFieldType.ListFloat64,           ParseListFloat64 },
            { PrototypeFieldType.ListEnum,              ParseListEnum },
            { PrototypeFieldType.ListAssetRef,          ParseListAssetRef },
            { PrototypeFieldType.ListAssetTypeRef,      ParseListAssetTypeRef },
            { PrototypeFieldType.ListPrototypeDataRef,  ParseListPrototypeDataRef },
            { PrototypeFieldType.ListPrototypePtr,      ParseListPrototypePtr },
            { PrototypeFieldType.PropertyCollection,    ParsePropertyList },
        };

        /// <summary>
        /// Returns a <see cref="ParseDelegate"/> for the specified <see cref="PrototypeFieldType"/> enum value.
        /// </summary>
        private static ParseDelegate GetParser(PrototypeFieldType prototypeFieldType)
        {
            if (ParseDelegateDict.TryGetValue(prototypeFieldType, out ParseDelegate parser) == false)
                return Logger.ErrorReturn<ParseDelegate>(null, $"GetParser(): Unsupported prototype field type {prototypeFieldType}");

            return parser;
        }

        /// <summary>
        /// Parses a <see cref="bool"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseBool(in FieldParserParams @params)
        {
            long rawValue = @params.Reader.ReadInt64();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, rawValue != 0);
            return true;
        }

        /// <summary>
        /// Parses an <see cref="sbyte"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseInt8(in FieldParserParams @params)
        {
            long rawValue = @params.Reader.ReadInt64();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, (sbyte)rawValue);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="short"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseInt16(in FieldParserParams @params)
        {
            long rawValue = @params.Reader.ReadInt64();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, (short)rawValue);
            return true;
        }

        /// <summary>
        /// Parses an <see cref="int"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseInt32(in FieldParserParams @params)
        {
            long rawValue = @params.Reader.ReadInt64();

            // Some prototypes (e.g. ProceduralProfileDrDoomPhase1.defaults) use very high values for int fields that cause overflows.
            // The client handles this by taking the first 4 bytes of the value and throwing away everything else.
            // We handle this by setting those fields to int.MaxValue, since the intention is apparently to have the value be as high
            // as possible. This doesn't seem to happen with other types.
            if (rawValue > int.MaxValue)
            {
                Logger.Trace($"ParseInt32(): Overflow for Int32 field {@params.BlueprintMemberInfo.Member.FieldName}, raw value {rawValue}, file name {@params.FileName}");
                rawValue = int.MaxValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, (int)rawValue);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="long"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseInt64(in FieldParserParams @params)
        {
            long rawValue = @params.Reader.ReadInt64();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, rawValue);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="float"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseFloat32(in FieldParserParams @params)
        {
            double rawValue = @params.Reader.ReadDouble();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, (float)rawValue);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="double"/> value and assigns it to a prototype field.
        /// </summary>
        private static bool ParseFloat64(in FieldParserParams @params)
        {
            double rawValue = @params.Reader.ReadDouble();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, rawValue);
            return true;
        }

        /// <summary>
        /// Parses an asset enum and assigns it to a prototype field.
        /// </summary>
        private static bool ParseEnum(in FieldParserParams @params)
        {
            // Enums are represented in Calligraphy by assets.
            // We get asset name from the serialized asset id, and then parse the actual enum value from it.
            AssetId assetId = (AssetId)@params.Reader.ReadUInt64();
            string assetName = GameDatabase.GetAssetName(assetId);

            // Fix asset names that start with a digit (C# doesn't allow enum members to start with a digit)
            if (assetName.Length > 0 && char.IsDigit(assetName[0]))
                assetName = $"_{assetName}";

            // Try to parse enum value from its name
            if (Enum.TryParse(@params.FieldInfo.PropertyType, assetName, true, out object value) == false)
            {
                if (assetName != string.Empty)
                    Logger.Trace(string.Format("ParseEnum(): Missing enum member {0} in {1}, field {2}, file name {3}",
                        assetName,
                        @params.FieldInfo.PropertyType.Name,
                        @params.BlueprintMemberInfo.Member.RuntimeClassFieldInfo.Name,
                        @params.FileName));

                // Set value to default for enums we can't parse
                AssetEnumAttribute attribute = @params.FieldInfo.PropertyType.GetCustomAttribute<AssetEnumAttribute>();
                @params.FieldInfo.SetValue(@params.OwnerPrototype, attribute.DefaultValue);
                return true;
            }

            // Set value to what we parsed if everything is okay
            @params.FieldInfo.SetValue(@params.OwnerPrototype, value);
            return true;
        }

        /// <summary>
        /// Parses a data reference and assigns it to a prototype field.
        /// </summary>
        private static bool ParseDataRef(in FieldParserParams @params)
        {
            // Data refs can be StringId, AssetTypeId, CurveId, PrototypeId, or LocaleStringId.
            // C# enums are not picky when assigning values with reflection, so we can reuse the same code for all of them.
            ulong value = @params.Reader.ReadUInt64();
            @params.FieldInfo.SetValue(@params.OwnerPrototype, value);
            return true;
        }

        /// <summary>
        /// Deserializes an embedded prototype and assigns it to a prototype field.
        /// </summary>
        private static bool ParsePrototypePtr(in FieldParserParams @params)
        {
            // The client nests multiple methods for deserializing embedded prototypes:
            // ParsePrototypePtr -> deserializePrototypePtr -> deserializePrototypePtrNoTemplate
            // We combine deserializePrototypePtr and deserializePrototypePtrNoTemplate in a single method.
            DeserializePrototypePtr(@params, false, out var prototype);
            @params.FieldInfo.SetValue(@params.OwnerPrototype, prototype);
            return true;
        }

        /// <summary>
        /// Deserializes an embedded prototype WITHOUT assigning it to a field.
        /// </summary>
        private static bool DeserializePrototypePtr(in FieldParserParams @params, bool polymorphicSetAllowed, out Prototype prototype)
        {
            prototype = null;
            BinaryReader reader = @params.Reader;

            // Parse header
            PrototypeDataHeader header = new(reader);

            if (header.ReferenceExists == false)
                return true;   // Early return if this is an empty prototype

            if (header.PolymorphicData && (polymorphicSetAllowed == false))
                return Logger.ErrorReturn(false, $"DeserializePrototypePtr(): Polymorphic prototype data encountered but not expected");
            
            // If this prototype has no data of its own, but it references a parent, we interpret it as its parent
            if (header.InstanceDataExists == false)
            {
                prototype = GameDatabase.GetPrototype<Prototype>(header.ReferenceType);
                return true;
            }

            // Deserialize
            Type classType = GameDatabase.DataDirectory.GetPrototypeClassType(header.ReferenceType);
            prototype = GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            DoDeserialize(prototype, header, PrototypeId.Invalid, @params.FileName, reader);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="PropertyId"/> and assigns it to a prototype field.
        /// </summary>
        private static bool ParsePropertyId(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;
            PrototypeDataHeader header = new(reader);

            if (header.InstanceDataExists)
            {
                short numFieldGroups = reader.ReadInt16();
                for (int i = 0; i < numFieldGroups; i++)
                {
                    BlueprintId groupBlueprintId = (BlueprintId)reader.ReadUInt64();
                    byte blueprintCopyNum = reader.ReadByte();

                    // Get the field group blueprint and make sure it is bound to a property
                    Blueprint groupBlueprint = GameDatabase.GetBlueprint(groupBlueprintId);
                    if (groupBlueprint.IsProperty == false)
                        return Logger.ErrorReturn(false, "ParsePropertyId(): Group blueprint is not bound to a property");

                    // Deserialize the property id and assign it to the field
                    PropertyId propertyId = PropertyId.Invalid;
                    DeserializeFieldGroupIntoPropertyId(ref propertyId, groupBlueprint, @params.FileName, reader, "Property List");
                    @params.FieldInfo.SetValue(@params.OwnerPrototype, propertyId);

                    // Same as in DeserializePropertyMixin(), there should be no list fields
                    short numListFields = reader.ReadInt16();
                    if (numListFields != 0)
                        return Logger.ErrorReturn(false, $"ParsePropertyId(): Property field group numListFields != 0");
                }
            }
            else if (header.ReferenceExists)
            {
                // If there is no data but a reference to a parent exists, get default property id from parent blueprint
                Blueprint parentBlueprint = GameDatabase.DataDirectory.GetPrototypeBlueprint(header.ReferenceType);

                if (parentBlueprint.IsProperty == false)
                    return Logger.ErrorReturn(false, "ParsePropertyId(): Parent blueprint is not bound to a property");

                PrototypeId propertyDataRef = parentBlueprint.PropertyDataRef;
                PropertyEnum propertyEnum = GameDatabase.PropertyInfoTable.GetPropertyEnumFromPrototype(propertyDataRef);

                if (propertyEnum == PropertyEnum.Invalid)
                    return Logger.ErrorReturn(false, "ParsePropertyId(): Parent property enum value is invalid");

                Properties.PropertyInfo info = GameDatabase.PropertyInfoTable.LookupPropertyInfo(propertyEnum);
                PropertyId defaultId = new(propertyEnum, info.DefaultParamValues);
                @params.FieldInfo.SetValue(@params.OwnerPrototype, defaultId);
            }

            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="bool"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListBool(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            bool[] values = new bool[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                long rawValue = reader.ReadInt64();
                values[i] = rawValue != 0;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="sbyte"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListInt8(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            sbyte[] values = new sbyte[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                long rawValue = reader.ReadInt64();
                values[i] = (sbyte)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="short"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListInt16(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            short[] values = new short[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                long rawValue = reader.ReadInt64();
                values[i] = (short)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="int"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListInt32(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            int[] values = new int[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                long rawValue = reader.ReadInt64();
                values[i] = (int)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="long"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListInt64(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            long[] values = new long[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                long rawValue = reader.ReadInt64();
                values[i] = rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="float"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListFloat32(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            float[] values = new float[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                double rawValue = reader.ReadDouble();
                values[i] = (float)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="double"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListFloat64(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            double[] values = new double[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                double rawValue = reader.ReadDouble();
                values[i] = rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of asset enum values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListEnum(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            // We have only type info for our enum, so we have to use Array.CreateInstance() to create our enum array
            Array values = Array.CreateInstance(@params.FieldInfo.PropertyType.GetElementType(), reader.ReadInt16());
            for (int i = 0; i < values.Length; i++)
            {
                // Enums are represented in Calligraphy by assets.
                // We get asset name from the serialized asset id, and then parse the actual enum value from it.
                AssetId assetId = (AssetId)reader.ReadUInt64();
                string assetName = GameDatabase.GetAssetName(assetId);

                // Looks like there are no numeric or invalid enum values in list enums, so we can speed this up
                // by just parsing whatever asset name we have as is.
                object value = Enum.Parse(@params.FieldInfo.PropertyType.GetElementType(), assetName, true);
                values.SetValue(value, i);
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="AssetId"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListAssetRef(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            AssetId[] values = new AssetId[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                ulong rawValue = reader.ReadUInt64();
                values[i] = (AssetId)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="AssetTypeId"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListAssetTypeRef(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            AssetTypeId[] values = new AssetTypeId[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                ulong rawValue = reader.ReadUInt64();
                values[i] = (AssetTypeId)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of <see cref="PrototypeId"/> values and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListPrototypeDataRef(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            PrototypeId[] values = new PrototypeId[reader.ReadInt16()];
            for (int i = 0; i < values.Length; i++)
            {
                ulong rawValue = reader.ReadUInt64();
                values[i] = (PrototypeId)rawValue;
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a collection of embedded prototypes and assigns it to a prototype field.
        /// </summary>
        private static bool ParseListPrototypePtr(in FieldParserParams @params)
        {
            BinaryReader reader = @params.Reader;

            Array values = Array.CreateInstance(@params.FieldInfo.PropertyType.GetElementType(), reader.ReadInt16());
            for (int i = 0; i < values.Length; i++)
            {
                DeserializePrototypePtr(@params, true, out Prototype prototype);
                values.SetValue(prototype, i);
            }

            @params.FieldInfo.SetValue(@params.OwnerPrototype, values);
            return true;
        }

        /// <summary>
        /// Parses a <see cref="PrototypePropertyCollection"/> from a serialized collection of embedded prototypes and assigns it to a prototype field.
        /// </summary>
        private static bool ParsePropertyList(in FieldParserParams @params)
        {
            // PropertyList seems to be used only in ModPrototype
            PrototypePropertyCollection propertyCollection = GetPropertyCollectionField(@params.OwnerPrototype);
            if (propertyCollection == null)
                return Logger.ErrorReturn(false, $"ParsePropertyList(): Failed to get a property collection, file name {@params.FileName}");

            BinaryReader reader = @params.Reader;

            short numItems = reader.ReadInt16();
            for (int i = 0; i < numItems; i++)
            {
                PrototypeDataHeader header = new(reader);
                if (header.InstanceDataExists == false) continue;

                short numFieldGroups = reader.ReadInt16();
                for (int j = 0; j < numFieldGroups; j++)
                {
                    BlueprintId groupBlueprintId = (BlueprintId)reader.ReadUInt64();
                    byte blueprintCopyNum = reader.ReadByte();

                    // Get the field group blueprint and make sure it is bound to a property
                    Blueprint groupBlueprint = GameDatabase.GetBlueprint(groupBlueprintId);
                    if (groupBlueprint.IsProperty == false)
                        return Logger.ErrorReturn(false, "ParsePropertyList(): Group blueprint is not bound to a property");

                    DeserializeFieldGroupIntoProperty(propertyCollection, groupBlueprint, blueprintCopyNum, @params.FileName, reader, "PropertyList");

                    short numListFields = reader.ReadInt16();
                    if (numListFields != 0)
                        return Logger.ErrorReturn(false, $"ParsePropertyList(): Property field group numListFields != 0");
                }
            }

            return true;
        }

        /// <summary>
        /// Contains parameters for field parsing methods.
        /// </summary>
        private readonly struct FieldParserParams
        {
            public readonly BinaryReader Reader;
            public readonly System.Reflection.PropertyInfo FieldInfo;
            public readonly Prototype OwnerPrototype;
            public readonly Blueprint OwnerBlueprint;
            public readonly string FileName;
            public readonly BlueprintMemberInfo BlueprintMemberInfo;

            public FieldParserParams(BinaryReader reader, System.Reflection.PropertyInfo fieldInfo, Prototype ownerPrototype,
                Blueprint ownerBlueprint, string fileName, BlueprintMemberInfo blueprintMemberInfo)
            {
                Reader = reader;
                FieldInfo = fieldInfo;
                OwnerPrototype = ownerPrototype;
                OwnerBlueprint = ownerBlueprint;
                FileName = fileName;
                BlueprintMemberInfo = blueprintMemberInfo;
            }
        }

        #endregion
    }
}
