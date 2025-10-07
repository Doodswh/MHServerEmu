using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MHServerEmu.Games.GameData.PatchManager
{
    /// <summary>
    /// Represents a single patch entry that can modify a prototype field
    /// </summary>
    public class PrototypePatchEntry
    {
        public bool Enabled { get; }
        public string Prototype { get; }
        public string Path { get; }
        public string Description { get; }
        public ValueBase Value { get; }

        [JsonIgnore]
        public string СlearPath { get; }
        [JsonIgnore]
        public string FieldName { get; }
        [JsonIgnore]
        public bool ArrayValue { get; }
        [JsonIgnore]
        public int ArrayIndex { get; }
        [JsonIgnore]
        public bool Patched { get; set; }

        [JsonConstructor]
        public PrototypePatchEntry(bool enabled, string prototype, string path, string description, ValueBase value)
        {
            Enabled = enabled;
            Prototype = prototype;
            Path = path;
            Description = description;
            Value = value;

            int lastDotIndex = path.LastIndexOf('.');
            if (lastDotIndex == -1)
            {
                СlearPath = string.Empty;
                FieldName = path;
            }
            else
            {
                СlearPath = path[..lastDotIndex];
                FieldName = path[(lastDotIndex + 1)..];
            }

            ArrayIndex = -1;
            ArrayValue = false;
            int index = FieldName.LastIndexOf('[');
            if (index != -1)
            {
                ArrayValue = true;

                int endIndex = FieldName.LastIndexOf(']');
                if (endIndex > index)
                {
                    string indexStr = FieldName.Substring(index + 1, endIndex - index - 1);
                    if (int.TryParse(indexStr, out int parsedIndex))
                        ArrayIndex = parsedIndex;
                }

                FieldName = FieldName[..index];
            }

            Patched = false;
        }
    }


    public class PatchEntryConverter : JsonConverter<PrototypePatchEntry>
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public override PrototypePatchEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string valueTypeString = root.GetProperty("ValueType").GetString();

            // Allow "Type[]" syntax in JSON for array types
            valueTypeString = valueTypeString.Replace("[]", "Array");

            var valueType = Enum.Parse<ValueType>(valueTypeString, true);
            var entry = new PrototypePatchEntry
            (
                root.GetProperty("Enabled").GetBoolean(),
                root.GetProperty("Prototype").GetString(),
                root.GetProperty("Path").GetString(),
                root.GetProperty("Description").GetString(),
                GetValueBase(root.GetProperty("Value"), valueType)
            );

            if (valueType == ValueType.Properties)
            {
                entry.Patched = true;
            }

            return entry;
        }

        public override void Write(Utf8JsonWriter writer, PrototypePatchEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("Enabled", value.Enabled);
            writer.WriteString("Prototype", value.Prototype);
            writer.WriteString("Path", value.Path);
            writer.WriteString("Description", value.Description);
            writer.WriteString("ValueType", value.Value.ValueType.ToString());
            writer.WritePropertyName("Value");
            JsonSerializer.Serialize(writer, value.Value.GetValue(), options);
            writer.WriteEndObject();
        }

        public static ValueBase GetValueBase(JsonElement jsonElement, ValueType valueType)
        {
            return valueType switch
            {
                // Simple value types
                ValueType.String => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.Boolean => new SimpleValue<bool>(jsonElement.GetBoolean(), valueType),
                ValueType.Float => new SimpleValue<float>(jsonElement.GetSingle(), valueType),
                ValueType.Integer => new SimpleValue<int>(jsonElement.GetInt32(), valueType),
                ValueType.Enum => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.PrototypeGuid => new SimpleValue<PrototypeGuid>((PrototypeGuid)jsonElement.GetUInt64(), valueType),
                ValueType.PrototypeId or ValueType.PrototypeDataRef => new SimpleValue<PrototypeId>((PrototypeId)jsonElement.GetUInt64(), valueType),
                ValueType.LocaleStringId => new SimpleValue<LocaleStringId>((LocaleStringId)jsonElement.GetUInt64(), valueType),
                ValueType.Vector3 => new SimpleValue<Vector3>(ParseJsonVector3(jsonElement), valueType),

                // Complex types - parse immediately for Eval and ComplexObject
                ValueType.Prototype => new SimpleValue<Prototype>(ParseJsonPrototype(jsonElement), valueType),
                ValueType.Properties => new SimpleValue<PropertyCollection>(ParseJsonProperties(jsonElement), valueType),
                ValueType.ComplexObject => new SimpleValue<Prototype>(ParseJsonComplexObject(jsonElement), valueType),
                ValueType.Eval => new SimpleValue<EvalPrototype>(ParseJsonEval(jsonElement), valueType),

                // Array types
                ValueType.PrototypeIdArray or ValueType.PrototypeDataRefArray => new ArrayValue<PrototypeId>(jsonElement, valueType, x => (PrototypeId)x.GetUInt64()),
                ValueType.PrototypeArray => new ArrayValue<Prototype>(jsonElement, valueType, x => ParseJsonPrototype(x)),
                ValueType.StringArray => new ArrayValue<string>(jsonElement, valueType, x => x.GetString()),
                ValueType.FloatArray => new ArrayValue<float>(jsonElement, valueType, x => x.GetSingle()),
                ValueType.IntegerArray => new ArrayValue<int>(jsonElement, valueType, x => x.GetInt32()),
                ValueType.BooleanArray => new ArrayValue<bool>(jsonElement, valueType, x => x.GetBoolean()),
                ValueType.Vector3Array => new ArrayValue<Vector3>(jsonElement, valueType, x => ParseJsonVector3(x)),
                ValueType.PropertyId => new SimpleValue<PropertyId>(ParseJsonPropertyIdSingle(jsonElement), valueType),

                _ => throw new NotSupportedException($"ValueType '{valueType}' is not supported.")
            };
        }

        private static Vector3 ParseJsonVector3(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("JSON element for Vector3 must be an array.");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length != 3)
                throw new InvalidOperationException($"JSON array for Vector3 must have 3 elements, but found {jsonArray.Length}.");

            return new Vector3(jsonArray[0].GetSingle(), jsonArray[1].GetSingle(), jsonArray[2].GetSingle());
        }

        /// <summary>
        /// Parses a ComplexObject from JSON - similar to Prototype but doesn't require ParentDataRef
        /// Used for rebuilding missing prototype sections
        /// </summary>
        public static Prototype ParseJsonComplexObject(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON element for ComplexObject parsing must be an object.");

            // ComplexObject can optionally have ParentDataRef, but doesn't require it
            PrototypeId referenceType = PrototypeId.Invalid;
            Type classType = null;

            if (jsonElement.TryGetProperty("ParentDataRef", out var parentDataRefElement))
            {
                referenceType = (PrototypeId)parentDataRefElement.GetUInt64();
                classType = GameDatabase.DataDirectory.GetPrototypeClassType(referenceType);
            }
            else if (jsonElement.TryGetProperty("ClassName", out var classNameElement))
            {
                // Allow specifying class type by name for ComplexObject
                string className = classNameElement.GetString();
                classType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByName(className);
            }

            if (classType == null)
            {
                Logger.Warn($"Could not determine class type for ComplexObject. Ensure ParentDataRef or ClassName is provided.");
                return null;
            }

            var prototype = GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            // Copy from reference if we have one
            if (referenceType != PrototypeId.Invalid)
            {
                CalligraphySerializer.CopyPrototypeDataRefFields(prototype, referenceType);
                prototype.ParentDataRef = referenceType;
            }

            // Apply all properties from JSON
            foreach (var property in jsonElement.EnumerateObject())
            {
                if (property.Name == "ParentDataRef" || property.Name == "ClassName")
                    continue;

                var fieldInfo = prototype.GetType().GetProperty(property.Name);
                if (fieldInfo == null)
                {
                    Logger.Warn($"Property '{property.Name}' not found on prototype type '{prototype.GetType().Name}'. Skipping.");
                    continue;
                }

                try
                {
                    object convertedValue = ParsePropertyValue(property.Value, fieldInfo);
                    fieldInfo.SetValue(prototype, convertedValue);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"Failed to parse or convert property '{property.Name}' for ComplexObject '{prototype.GetType().Name}'.");
                }
            }

            return prototype;
        }

        /// <summary>
        /// Parses an Eval prototype from JSON with full support for nested structures
        /// </summary>
        public static EvalPrototype ParseJsonEval(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON element for Eval parsing must be an object.");

            if (!jsonElement.TryGetProperty("ParentDataRef", out var parentDataRefElement))
                throw new InvalidOperationException("JSON element for Eval is missing the required 'ParentDataRef' property.");

            var referenceType = (PrototypeId)parentDataRefElement.GetUInt64();
            Type classType = GameDatabase.DataDirectory.GetPrototypeClassType(referenceType);

            if (classType == null)
            {
                Logger.Warn($"Could not find class type for Eval prototype ID '{referenceType}'.");
                return null;
            }

            // Ensure it's actually an EvalPrototype
            if (!typeof(EvalPrototype).IsAssignableFrom(classType))
            {
                Logger.Warn($"Class type '{classType.Name}' for prototype '{referenceType}' is not an EvalPrototype.");
                return null;
            }

            var evalPrototype = (EvalPrototype)GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            CalligraphySerializer.CopyPrototypeDataRefFields(evalPrototype, referenceType);
            evalPrototype.ParentDataRef = referenceType;

            // Parse all properties
            foreach (var property in jsonElement.EnumerateObject())
            {
                if (property.Name == "ParentDataRef") continue;

                var fieldInfo = evalPrototype.GetType().GetProperty(property.Name);
                if (fieldInfo == null)
                {
                    Logger.Warn($"Property '{property.Name}' not found on Eval type '{evalPrototype.GetType().Name}'. Skipping.");
                    continue;
                }

                try
                {
                    object convertedValue = ParsePropertyValue(property.Value, fieldInfo);
                    fieldInfo.SetValue(evalPrototype, convertedValue);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"Failed to parse or convert property '{property.Name}' for Eval '{evalPrototype.GetType().Name}'.");
                }
            }

            return evalPrototype;
        }

        public static Prototype ParseJsonPrototype(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON element for Prototype parsing must be an object.");

            if (!jsonElement.TryGetProperty("ParentDataRef", out var parentDataRefElement))
                throw new InvalidOperationException("JSON element for Prototype is missing the required 'ParentDataRef' property.");

            var referenceType = (PrototypeId)parentDataRefElement.GetUInt64();
            Type classType = GameDatabase.DataDirectory.GetPrototypeClassType(referenceType);

            if (classType == null)
            {
                Logger.Warn($"Could not find class type for prototype ID '{referenceType}'.");
                return null;
            }

            var prototype = GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            CalligraphySerializer.CopyPrototypeDataRefFields(prototype, referenceType);
            prototype.ParentDataRef = referenceType;

            foreach (var property in jsonElement.EnumerateObject())
            {
                if (property.Name == "ParentDataRef") continue;

                var fieldInfo = prototype.GetType().GetProperty(property.Name);
                if (fieldInfo == null)
                {
                    Logger.Warn($"Property '{property.Name}' not found on prototype type '{prototype.GetType().Name}'. Skipping.");
                    continue;
                }

                try
                {
                    object convertedValue = ParsePropertyValue(property.Value, fieldInfo);
                    fieldInfo.SetValue(prototype, convertedValue);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"Failed to parse or convert property '{property.Name}' for prototype '{prototype.GetType().Name}'.");
                }
            }

            return prototype;
        }

        /// <summary>
        /// Unified property value parser that handles all types including nested prototypes
        /// </summary>
        private static object ParsePropertyValue(JsonElement propertyValue, System.Reflection.PropertyInfo fieldInfo)
        {
            Type targetType = fieldInfo.PropertyType;

            // Handle PropertyId with special structure
            if (targetType == typeof(PropertyId) && propertyValue.ValueKind == JsonValueKind.Object)
            {
                if (propertyValue.TryGetProperty("ParentDataRef", out var idElement))
                {
                    var propId = (PrototypeId)idElement.GetUInt64();
                    var propEnum = GameDatabase.PropertyInfoTable.GetPropertyEnumFromPrototype(propId);

                    if (propEnum != PropertyEnum.Invalid)
                    {
                        return new PropertyId(propEnum);
                    }
                    else
                    {
                        Logger.Warn($"Could not find a valid PropertyEnum for PrototypeId '{propId}'.");
                        return null;
                    }
                }
            }

            // Handle nested EvalPrototype
            if (typeof(EvalPrototype).IsAssignableFrom(targetType) && propertyValue.ValueKind == JsonValueKind.Object)
            {
                return ParseJsonEval(propertyValue);
            }

            // Handle nested Prototype
            if (typeof(Prototype).IsAssignableFrom(targetType) && propertyValue.ValueKind == JsonValueKind.Object)
            {
                return ParseJsonPrototype(propertyValue);
            }

            // Handle arrays of prototypes
            if (targetType.IsArray && propertyValue.ValueKind == JsonValueKind.Array)
            {
                Type elementType = targetType.GetElementType();
                var jsonArray = propertyValue.EnumerateArray().ToArray();
                Array resultArray = Array.CreateInstance(elementType, jsonArray.Length);

                for (int i = 0; i < jsonArray.Length; i++)
                {
                    object element;

                    if (typeof(EvalPrototype).IsAssignableFrom(elementType) && jsonArray[i].ValueKind == JsonValueKind.Object)
                    {
                        element = ParseJsonEval(jsonArray[i]);
                    }
                    else if (typeof(Prototype).IsAssignableFrom(elementType) && jsonArray[i].ValueKind == JsonValueKind.Object)
                    {
                        element = ParseJsonPrototype(jsonArray[i]);
                    }
                    else
                    {
                        element = PrototypePatchManager.ConvertValue(ParseJsonElement(jsonArray[i], elementType), elementType);
                    }

                    resultArray.SetValue(element, i);
                }

                return resultArray;
            }

            // Handle simple types
            object parsedElement = ParseJsonElement(propertyValue, targetType);
            return PrototypePatchManager.ConvertValue(parsedElement, targetType);
        }

        private static PropertyId ParseJsonPropertyIdSingle(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON element for PropertyId must be an object.");

            if (!jsonElement.TryGetProperty("PropertyEnum", out var propEnumElement))
                throw new InvalidOperationException("PropertyId JSON must contain 'PropertyEnum' field.");

            var propertyEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), propEnumElement.GetString(), true);
            var infoTable = GameDatabase.PropertyInfoTable;
            PropertyInfo propertyInfo = infoTable.LookupPropertyInfo(propertyEnum);

            return ParseJsonPropertyId(jsonElement, propertyEnum, propertyInfo);
        }

        public static PropertyCollection ParseJsonProperties(JsonElement jsonElement)
        {
            PropertyCollection properties = new();
            var infoTable = GameDatabase.PropertyInfoTable;

            foreach (var property in jsonElement.EnumerateObject())
            {
                try
                {
                    var propEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), property.Name);
                    PropertyInfo propertyInfo = infoTable.LookupPropertyInfo(propEnum);
                    PropertyId propId = ParseJsonPropertyId(property.Value, propEnum, propertyInfo);
                    PropertyValue propValue = ParseJsonPropertyValue(property.Value, propertyInfo);
                    properties.SetProperty(propValue, propId);
                }
                catch (Exception ex)
                {
                    Logger.WarnException(ex, $"Failed to parse property '{property.Name}': {ex.Message}");
                }
            }

            return properties;
        }

        public static object ParseAndValidateEnum(JsonElement jsonElement, Type enumType)
        {
            if (!enumType.IsEnum)
                throw new InvalidOperationException($"Type '{enumType.Name}' is not an enum type.");

            object enumValue;

            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                string enumString = jsonElement.GetString();
                if (!Enum.TryParse(enumType, enumString, true, out enumValue))
                {
                    var validValues = string.Join(", ", Enum.GetNames(enumType));
                    throw new InvalidOperationException(
                        $"Invalid enum value '{enumString}' for type '{enumType.Name}'. Valid values are: {validValues}");
                }
            }
            else if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                int numericValue = jsonElement.GetInt32();
                if (!Enum.IsDefined(enumType, numericValue))
                {
                    Logger.Warn($"Numeric enum value {numericValue} is not defined in '{enumType.Name}'. Using it anyway.");
                }
                enumValue = Enum.ToObject(enumType, numericValue);
            }
            else
            {
                throw new InvalidOperationException(
                    $"JSON element for enum '{enumType.Name}' must be a string or number, but was {jsonElement.ValueKind}.");
            }

            return enumValue;
        }

        public static PropertyId ParseJsonPropertyId(JsonElement jsonElement, PropertyEnum propEnum, PropertyInfo propInfo)
        {
            int paramCount = propInfo.ParamCount;
            if (paramCount == 0) return new(propEnum);

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            Span<PropertyParam> paramValues = stackalloc PropertyParam[Property.MaxParamCount];
            propInfo.DefaultParamValues.CopyTo(paramValues);

            for (int i = 0; i < paramCount && i < Property.MaxParamCount; i++)
            {
                if (i >= jsonArray.Length) continue;
                var paramValue = jsonArray[i];

                switch (propInfo.GetParamType(i))
                {
                    case PropertyParamType.Asset:
                        paramValues[i] = Property.ToParam((AssetId)ParseJsonElement(paramValue, typeof(AssetId)));
                        break;
                    case PropertyParamType.Prototype:
                        paramValues[i] = Property.ToParam(propEnum, i, (PrototypeId)ParseJsonElement(paramValue, typeof(PrototypeId)));
                        break;
                    case PropertyParamType.Integer:
                        if (paramValue.TryGetInt32(out int intValue))
                            paramValues[i] = (PropertyParam)intValue;
                        break;
                    default:
                        Logger.Warn($"Unsupported PropertyParamType: {propInfo.GetParamType(i)} for property {propEnum}");
                        break;
                }
            }

            return new PropertyId(propEnum, paramValues[0], paramValues[1], paramValues[2], paramValues[3]);
        }

        public static PropertyValue ParseJsonPropertyValue(JsonElement jsonElement, PropertyInfo propInfo)
        {
            if (propInfo.ParamCount > 0 && jsonElement.ValueKind == JsonValueKind.Array)
            {
                var jsonArray = jsonElement.EnumerateArray().ToArray();
                if (jsonArray.Length > 0)
                {
                    jsonElement = jsonArray[^1];
                }
            }

            return propInfo.DataType switch
            {
                PropertyDataType.Integer => new PropertyValue(jsonElement.GetInt64()),
                PropertyDataType.Real => new PropertyValue(jsonElement.GetSingle()),
                PropertyDataType.Boolean => new PropertyValue(jsonElement.GetBoolean()),
                PropertyDataType.Prototype => new PropertyValue((PrototypeId)ParseJsonElement(jsonElement, typeof(PrototypeId))),
                PropertyDataType.Asset => new PropertyValue((AssetId)ParseJsonElement(jsonElement, typeof(AssetId))),
                _ => throw new InvalidOperationException($"Unsupported PropertyDataType '{propInfo.DataType}' for property '{propInfo.PropertyName}'.")
            };
        }

        public static object ParseJsonElement(JsonElement value, Type targetType)
        {
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
                return null;

            if (targetType == typeof(string)) return value.GetString();
            if (targetType == typeof(bool) || targetType == typeof(bool?)) return value.GetBoolean();
            if (targetType == typeof(int) || targetType == typeof(int?)) return value.GetInt32();
            if (targetType == typeof(long) || targetType == typeof(long?)) return value.GetInt64();
            if (targetType == typeof(float) || targetType == typeof(float?)) return value.GetSingle();
            if (targetType == typeof(double) || targetType == typeof(double?)) return value.GetDouble();
            if (targetType == typeof(ulong) || targetType == typeof(ulong?)) return value.GetUInt64();
            if (targetType == typeof(PrototypeId)) return (PrototypeId)value.GetUInt64();
            if (targetType == typeof(AssetId)) return (AssetId)value.GetUInt64();
            if (targetType == typeof(LocaleStringId)) return (LocaleStringId)value.GetUInt64();
            if (targetType == typeof(PrototypeGuid)) return (PrototypeGuid)value.GetUInt64();
            if (targetType.IsEnum)
            {
                if (value.ValueKind == JsonValueKind.String)
                    return Enum.Parse(targetType, value.GetString(), true);
                if (value.ValueKind == JsonValueKind.Number)
                    return Enum.ToObject(targetType, value.GetInt32());
            }

            // For complex objects or arrays, return the JsonElement for later processing
            if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }

            return value.ToString();
        }
    }

    public enum ValueType
    {
        // Simple Types
        String,
        Boolean,
        Float,
        Integer,
        Enum,
        PrototypeGuid,
        PrototypeId,
        LocaleStringId,
        PrototypeDataRef,
        Vector3,
        PropertyId,
        Eval,

        // Complex Types
        Prototype,
        Properties,
        ComplexObject,

        // Array Types
        StringArray,
        BooleanArray,
        FloatArray,
        IntegerArray,
        PrototypeIdArray,
        PrototypeDataRefArray,
        PrototypeArray,
        Vector3Array,
    }

    public abstract class ValueBase
    {
        public abstract ValueType ValueType { get; }
        public abstract object GetValue();
    }

    public class SimpleValue<T> : ValueBase
    {
        public override ValueType ValueType { get; }
        public T Value { get; }

        public SimpleValue(T value, ValueType valueType)
        {
            Value = value;
            ValueType = valueType;
        }

        public override object GetValue() => Value;
    }

    public class ArrayValue<T> : SimpleValue<T[]>
    {
        public ArrayValue(JsonElement jsonElement, ValueType valueType, Func<JsonElement, T> elementParser)
            : base(ParseJsonArray(jsonElement, elementParser), valueType) { }

        private static T[] ParseJsonArray(JsonElement jsonElement, Func<JsonElement, T> elementParser)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("JSON element is not an array and cannot be parsed as an ArrayValue.");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length == 0) return Array.Empty<T>();

            var result = new T[jsonArray.Length];
            for (int i = 0; i < jsonArray.Length; i++)
            {
                result[i] = elementParser(jsonArray[i]);
            }

            return result;
        }
    }
}