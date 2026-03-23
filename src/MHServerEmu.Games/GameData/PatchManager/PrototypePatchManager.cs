using MHServerEmu.Core.Helpers;
using MHServerEmu.Games.GameData.Prototypes;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using MHServerEmu.Games.GameData.Calligraphy;
using System.Collections.Generic;
using System.Linq;
using System;
using MHServerEmu.Games.Properties;
using System.Collections;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchManager
    {

        private readonly Stack<PrototypeId> _protoStack = new();
        private readonly Dictionary<PrototypeId, List<PrototypePatchEntry>> _patchDict = new();
        private readonly Dictionary<Prototype, string> _pathDict = new();
        private bool _initialized = false;

        public static PrototypePatchManager Instance { get; } = new();

        public void Initialize(bool enablePatchManager)
        {
            if (enablePatchManager && !_initialized)
            {
                _initialized = LoadPatchDataFromDisk();
            }
        }

        private bool LoadPatchDataFromDisk()
        {
            _patchDict.Clear();
            _pathDict.Clear();
            _protoStack.Clear();

            string patchDirectory = Path.Combine(FileHelper.DataDirectory, "Game", "Patches");
            if (!Directory.Exists(patchDirectory))
                return false;
            int count = 0;
            int skippedDisabled = 0;
            int skippedInvalid = 0;
            var options = new JsonSerializerOptions { Converters = { new PatchEntryConverter() } };

            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, "PatchData", "json"))
            {
                string fileName = Path.GetFileName(filePath);

                try
                {
                    PrototypePatchEntry[] updateValues = FileHelper.DeserializeJson<PrototypePatchEntry[]>(filePath, options);
                    if (updateValues == null)
                    {
                        continue;
                    }

                    foreach (PrototypePatchEntry value in updateValues)
                    {
                        if (!value.Enabled)
                        {
                            skippedDisabled++;
                            continue;
                        }

                        PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(value.Prototype);
                        if (prototypeId == PrototypeId.Invalid)
                        {
                            skippedInvalid++;
                            continue;
                        }

                        AddPatchValue(prototypeId, value);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                }
            }

            return true;
        }

        private void AddPatchValue(PrototypeId prototypeId, in PrototypePatchEntry value)
        {
            if (!_patchDict.TryGetValue(prototypeId, out var patchList))
            {
                patchList = new List<PrototypePatchEntry>();
                _patchDict[prototypeId] = patchList;
            }
            patchList.Add(value);
        }

        public bool CheckProperties(PrototypeId protoRef, out PropertyCollection prop)
        {
            prop = null;
            if (!_initialized) return false;

            if (_patchDict.TryGetValue(protoRef, out var list))
            {
                foreach (var entry in list)
                {
                    if (entry.Value.ValueType == ValueType.Properties)
                    {
                        prop = entry.Value.GetValue() as PropertyCollection;
                        if (prop != null)
                        {
                            entry.Patched = true;
                            return true;
                        }
                        else
                        {
                        }
                    }
                }
            }
            return false;
        }

        public bool PreCheck(PrototypeId protoRef)
        {
            if (!_initialized) return false;

            if (protoRef != PrototypeId.Invalid && _patchDict.TryGetValue(protoRef, out var list))
            {
                if (list.Any(e => !e.Patched))
                {
                    _protoStack.Push(protoRef);
                    return true;
                }
            }
            return false;
        }

        public void PostOverride(Prototype prototype)
        {
            if (_protoStack.Count == 0) return;

            string currentPath = string.Empty;
            if (prototype.DataRef == PrototypeId.Invalid && !_pathDict.TryGetValue(prototype, out currentPath))
            {
                return;
            }

            PrototypeId patchProtoRef = _protoStack.Peek();
            if (prototype.DataRef != PrototypeId.Invalid)
            {
                if (prototype.DataRef != patchProtoRef)
                {
                    return;
                }
                if (_patchDict.ContainsKey(prototype.DataRef))
                {
                    patchProtoRef = _protoStack.Pop();
                }
            }

            if (!_patchDict.TryGetValue(patchProtoRef, out var list))
            {
                return;
            }

            int appliedCount = 0;
            foreach (var entry in list)
            {
                if (!entry.Patched)
                {
                    if (CheckAndUpdate(entry, prototype))
                    {
                        appliedCount++;
                    }
                    else
                    {
                    }
                }
            }

            if (appliedCount > 0)

            if (_protoStack.Count == 0)
            {
                _pathDict.Clear();
            }
        }

        private bool CheckAndUpdate(PrototypePatchEntry entry, Prototype prototype)
        {

            // Navigate to the target object using the path
            var targetObject = GetOrCreateObjectFromPath(prototype, entry.СlearPath);
            if (targetObject == null)
            {
                return false;
            }


            // Use the enhanced field lookup from PrototypeClassManager
            var enhancedField = GameDatabase.PrototypeClassManager.GetFieldByName(targetObject.GetType(), entry.FieldName);
            if (!enhancedField.HasValue)
            {
                return false;
            }

            var fieldInfo = enhancedField.Value;
            if (entry.Value.ValueType == ValueType.Properties && fieldInfo.PropertyInfo.PropertyType == typeof(PropertyCollection))
            {
                var targetPropCollection = (PropertyCollection)fieldInfo.PropertyInfo.GetValue(targetObject);
                var patchPropCollection = (PropertyCollection)entry.Value.GetValue();

                if (targetPropCollection != null && patchPropCollection != null)
                {
                    // Merge the patched properties into the existing collection
                    foreach (var kvp in patchPropCollection)
                    {
                        // SetProperty natively overwrites the existing value for this specific PropertyId
                        targetPropCollection.SetProperty(kvp.Value, kvp.Key);
                    }
                    entry.Patched = true;
                    return true;
                }
            }

            // Validate field writability
            if (!fieldInfo.CanWrite)
            {
                return false;
            }

            // Apply the update
            return UpdateValue(targetObject, fieldInfo, entry);
        }

        private List<string> GetAvailableProperties(object obj)
        {
            if (obj == null) return new List<string> { "null object" };

            return GameDatabase.PrototypeClassManager.GetAvailablePropertyNames(obj.GetType()).ToList();
        }

        private bool UpdateValue(object targetObject, PrototypeClassManager.EnhancedFieldInfo fieldInfo, PrototypePatchEntry entry)
        {
            try
            {
                if (entry.ArrayValue)
                {
                    if (entry.ArrayIndex != -1)
                    {
                        SetIndexValue(targetObject, fieldInfo, entry.ArrayIndex, entry.Value);
                    }
                    else
                    {
                        InsertValue(targetObject, fieldInfo, entry.Value);
                    }
                }
                else
                {
                    object rawValue = entry.Value.GetValue();

                    object convertedValue = ConvertValue(rawValue, fieldInfo.PropertyInfo.PropertyType);

                    // Special handling for Eval and ComplexObject - they're already parsed
                    if (entry.Value.ValueType == ValueType.Eval || entry.Value.ValueType == ValueType.ComplexObject)
                    {
                    }
                    else
                    {
                    }

                    // Use the centralized property setting method
                    if (!GameDatabase.PrototypeClassManager.TrySetPropertyValue(targetObject, fieldInfo.PropertyInfo, convertedValue))
                    {
                        return false;
                    }
                }

                entry.Patched = true;
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public void SetPath(Prototype parent, Prototype child, string fieldName)
        {
            if (child == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
            {
                parentPath = string.Empty;
            }
            string newPath = string.IsNullOrEmpty(parentPath) ? fieldName : $"{parentPath}.{fieldName}";
            _pathDict[child] = newPath;
        }

        public void SetPathIndex(Prototype parent, Prototype child, string fieldName, int index)
        {
            if (child == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
            {
                parentPath = string.Empty;
            }
            string newPath = string.IsNullOrEmpty(parentPath) ? $"{fieldName}[{index}]" : $"{parentPath}.{fieldName}[{index}]";
            _pathDict[child] = newPath;
        }

        private static void SetIndexValue(object target, PrototypeClassManager.EnhancedFieldInfo fieldInfo, int index, ValueBase value)
        {
            var propertyInfo = fieldInfo.PropertyInfo;

            if (propertyInfo.GetValue(target) is not Array array)
            {
                return;
            }

            if (index < 0 || index >= array.Length)
            {
                return;
            }

            // Use the cached element type from EnhancedFieldInfo
            Type elementType = fieldInfo.ElementType;

            object valueEntry = value.GetValue();
            object finalValue;


            if (typeof(Prototype).IsAssignableFrom(elementType) && (valueEntry is PrototypeId || valueEntry is ulong || value.ValueType == ValueType.PrototypeDataRef))
            {
                var dataRef = (PrototypeId)Convert.ChangeType(valueEntry, typeof(ulong));
                finalValue = GameDatabase.GetPrototype<Prototype>(dataRef);

                if (finalValue == null)
                {
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype or could not be found.");
                }
            }
            else if (typeof(EvalPrototype).IsAssignableFrom(elementType) && valueEntry is EvalPrototype)
            {
                finalValue = valueEntry;
            }
            else if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is Prototype)
            {
                finalValue = valueEntry;
            }
            else
            {
                finalValue = ConvertValue(valueEntry, elementType);
            }

            if (!elementType.IsAssignableFrom(finalValue.GetType()))
            {
                throw new InvalidOperationException($"The resolved value of type {finalValue.GetType().Name} cannot be assigned to an array element of type {elementType.Name}.");
            }

            array.SetValue(finalValue, index);
        }

        private static void InsertValue(object target, PrototypeClassManager.EnhancedFieldInfo fieldInfo, ValueBase value)
        {
            var propertyInfo = fieldInfo.PropertyInfo;


            if (!fieldInfo.IsArray)
                throw new InvalidOperationException($"Field {propertyInfo.Name} is not an array.");

            // Use the cached element type from EnhancedFieldInfo
            Type elementType = fieldInfo.ElementType;

            var valueEntry = value.GetValue();
            var currentArray = (Array)propertyInfo.GetValue(target);
            var valuesToAdd = valueEntry as Array;


            if (valuesToAdd != null)
            {
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + valuesToAdd.Length);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                for (int i = 0; i < valuesToAdd.Length; i++)
                {
                    object element = GetElementValue(valuesToAdd.GetValue(i), elementType);
                    newArray.SetValue(element, (currentArray?.Length ?? 0) + i);
                }
                propertyInfo.SetValue(target, newArray);
            }
            else
            {
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + 1);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                object element = GetElementValue(valueEntry, elementType);
                newArray.SetValue(element, newArray.Length - 1);
                propertyInfo.SetValue(target, newArray);
            }
        }

        private object GetOrCreateObjectFromPath(object root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;

            object current = root;
            var pathParts = path.Split('.');

            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (current == null) return null;

                if (part.Contains("[\"") || part.Contains("['"))
                {
                    int bracketStart = part.IndexOf('[');
                    string memberName = part.Substring(0, bracketStart);

                    object memberValue = GetMemberValue(current, memberName, out Type memberType);
                    if (memberValue == null && memberType == null) return null;

                    var dictObj = memberValue as IDictionary;
                    if (dictObj == null) return null;

                    var keyMatch = System.Text.RegularExpressions.Regex.Match(part, @"\[[""'](.+?)[""']\]");
                    if (keyMatch.Success)
                    {
                        string key = keyMatch.Groups[1].Value;
                        if (dictObj.Contains(key))
                        {
                            current = dictObj[key];
                            if (current is PrototypeId pidDict && pidDict != PrototypeId.Invalid)
                            {
                                var resolved = GameDatabase.GetPrototype<Prototype>(pidDict);
                                if (resolved != null) current = resolved;
                            }
                            continue;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                if (part.Contains("["))
                {
                    int bracketStart = part.IndexOf('[');
                    string memberName = part.Substring(0, bracketStart);

                    object listObj = GetMemberValue(current, memberName, out Type memberType);

                    if (listObj == null)
                    {
                        if (memberType == null)
                        {
                            return null;
                        }
                        return null;
                    }

                    var matches = System.Text.RegularExpressions.Regex.Matches(part.Substring(bracketStart), @"\[(\d+)\]");
                    current = listObj;

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (!match.Success) continue;
                        int index = int.Parse(match.Groups[1].Value);

                        if (current is IList list && index < list.Count)
                        {
                            current = list[index];
                            if (current is PrototypeId pidList && pidList != PrototypeId.Invalid)
                            {
                                var resolved = GameDatabase.GetPrototype<Prototype>(pidList);
                                if (resolved != null) current = resolved;
                            }
                        }
                        else if (current is Array array && index < array.Length)
                        {
                            current = array.GetValue(index);
                            if (current is PrototypeId pidArr && pidArr != PrototypeId.Invalid)
                            {
                                var resolved = GameDatabase.GetPrototype<Prototype>(pidArr);
                                if (resolved != null) current = resolved;
                            }
                        }
                        else
                        {
                            return null;
                        }
                    }
                    continue;
                }

                object nextObj = GetMemberValue(current, part, out Type nextMemberType);
                if (nextObj == null && nextMemberType == null)
                {
                    return null;
                }

                if (nextObj is PrototypeId pid && pid != PrototypeId.Invalid)
                {
                    var resolvedProto = GameDatabase.GetPrototype<Prototype>(pid);
                    if (resolvedProto != null)
                    {
                        nextObj = resolvedProto;
                    }
                }

                if (nextObj == null && nextMemberType != null)
                {
                    try
                    {
                        nextObj = Activator.CreateInstance(nextMemberType);
                        SetMemberValue(current, part, nextObj);
                    }
                    catch { return null; }
                }
                current = nextObj;
            }

            return current;
        }

        // --- NEW HELPER METHODS ---

        private object GetMemberValue(object obj, string memberName, out Type memberType)
        {
            memberType = null;
            if (obj == null) return null;

            var type = obj.GetType();

            // 1. Try Property
            var propInfo = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (propInfo != null)
            {
                memberType = propInfo.PropertyType;
                return propInfo.GetValue(obj);
            }

            // 2. Try Field (This fixes your Mixin issue)
            var fieldInfo = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo != null)
            {
                memberType = fieldInfo.FieldType;
                return fieldInfo.GetValue(obj);
            }

            return null;
        }

        private void SetMemberValue(object obj, string memberName, object value)
        {
            if (obj == null) return;
            var type = obj.GetType();

            var propInfo = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (propInfo != null && propInfo.CanWrite)
            {
                propInfo.SetValue(obj, value);
                return;
            }

            var fieldInfo = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(obj, value);
            }
        }

        private static object GetElementValue(object valueEntry, Type elementType)
        {

            // Handle already-parsed prototypes
            if (typeof(EvalPrototype).IsAssignableFrom(elementType) && valueEntry is EvalPrototype)
            {
                return valueEntry;
            }

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is Prototype)
            {
                return valueEntry;
            }

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is PrototypeId dataRef)
            {
                var result = GameDatabase.GetPrototype<Prototype>(dataRef);
                if (result == null)
                {
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype.");
                }
                return result;
            }

            return ConvertValue(valueEntry, elementType);
        }

        public static object ConvertValue(object rawValue, Type targetType)
        {

            if (rawValue == null || (rawValue is JsonElement jsonValCheck && jsonValCheck.ValueKind == JsonValueKind.Null))
            {
                return null;
            }

            if (targetType.IsInstanceOfType(rawValue))
            {
                return rawValue;
            }

            // Handle already-parsed EvalPrototype
            if (typeof(EvalPrototype).IsAssignableFrom(targetType) && rawValue is EvalPrototype evalProto)
            {
                return evalProto;
            }

            // Handle already-parsed Prototype (ComplexObject)
            if (typeof(Prototype).IsAssignableFrom(targetType) && rawValue is Prototype proto)
            {
                return proto;
            }

            if (targetType == typeof(PropertyId) && rawValue is JsonElement jsonValForPropId && jsonValForPropId.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    if (jsonValForPropId.TryGetProperty("PropertyEnum", out var propEnumElement))
                    {
                        var propertyEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), propEnumElement.GetString(), true);

                        PropertyParam[] parameters = new PropertyParam[Property.MaxParamCount];
                        for (int i = 0; i < Property.MaxParamCount; i++)
                        {
                            if (jsonValForPropId.TryGetProperty($"Param{i}", out var paramElement))
                            {
                                parameters[i] = (PropertyParam)paramElement.GetUInt64();
                            }
                        }

                        return new PropertyId(propertyEnum, parameters[0], parameters[1], parameters[2], parameters[3]);
                    }
                }
                catch (Exception ex)
                {
                    return null;
                }
            }

            if (rawValue is JsonElement jsonVal)
            {
                // Handle arrays
                if (jsonVal.ValueKind == JsonValueKind.Array && targetType.IsArray)
                {
                    Type elementType = targetType.GetElementType();
                    var jsonArray = jsonVal.EnumerateArray().ToArray();

                    Array newArray = Array.CreateInstance(elementType, jsonArray.Length);
                    for (int i = 0; i < jsonArray.Length; i++)
                    {
                        newArray.SetValue(ConvertValue(jsonArray[i], elementType), i);
                    }
                    return newArray;
                }

                // Handle EvalPrototype from JSON
                if (typeof(EvalPrototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                    {
                        return PatchEntryConverter.ParseJsonEval(jsonVal);
                    }
                }

                // Handle Prototype from JSON
                if (typeof(Prototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                    {
                        return PatchEntryConverter.ParseJsonPrototype(jsonVal);
                    }
                }

                // Handle PropertyId from JSON object
                if (targetType == typeof(PropertyId) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        if (jsonVal.TryGetProperty("PropertyEnum", out var propEnumElement))
                        {
                            var propertyEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), propEnumElement.GetString(), true);

                            PropertyParam[] parameters = new PropertyParam[Property.MaxParamCount];
                            for (int i = 0; i < Property.MaxParamCount; i++)
                            {
                                if (jsonVal.TryGetProperty($"Param{i}", out var paramElement))
                                {
                                    parameters[i] = (PropertyParam)paramElement.GetUInt64();
                                }
                            }

                            return new PropertyId(propertyEnum, parameters[0], parameters[1], parameters[2], parameters[3]);
                        }
                    }
                    catch (Exception ex)
                    {
                        return null;
                    }
                }

                // Handle simple primitive types from a JsonElement
                try
                {
                    if (targetType == typeof(string)) return jsonVal.GetString();
                    if (targetType == typeof(int)) return jsonVal.GetInt32();
                    if (targetType == typeof(long)) return jsonVal.GetInt64();
                    if (targetType == typeof(ulong)) return jsonVal.GetUInt64();
                    if (targetType == typeof(float)) return jsonVal.GetSingle();
                    if (targetType == typeof(double)) return jsonVal.GetDouble();
                    if (targetType == typeof(bool)) return jsonVal.GetBoolean();
                    if (targetType == typeof(PrototypeId)) return (PrototypeId)jsonVal.GetUInt64();
                    if (targetType == typeof(LocaleStringId)) return (LocaleStringId)jsonVal.GetUInt64();
                    if (targetType == typeof(AssetId)) return (AssetId)jsonVal.GetUInt64();
                    if (targetType == typeof(PrototypeGuid)) return (PrototypeGuid)jsonVal.GetUInt64();
                    if (targetType.IsEnum)
                    {
                        return PatchEntryConverter.ParseAndValidateEnum(jsonVal, targetType);
                    }
                }
                catch (Exception ex)
                {
                }
            }

            // Fallback for other conversions
            if (rawValue is JsonElement[] jsonElementArray && targetType.IsArray)
            {
                Type elementType = targetType.GetElementType();
                Array newArray = Array.CreateInstance(elementType, jsonElementArray.Length);
                for (int i = 0; i < jsonElementArray.Length; i++)
                {
                    newArray.SetValue(ConvertValue(jsonElementArray[i], elementType), i);
                }
                return newArray;
            }

            if (targetType == typeof(AssetId) && rawValue is string assetString)
            {
                int typeNameStart = assetString.LastIndexOf('(');
                int typeNameEnd = assetString.LastIndexOf(')');

                if (typeNameStart != -1 && typeNameEnd > typeNameStart)
                {
                    string assetName = assetString.Substring(0, typeNameStart).Trim();
                    string assetTypeName = assetString.Substring(typeNameStart + 1, typeNameEnd - typeNameStart - 1).Trim();

                    if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(assetTypeName))
                    {
                        return AssetId.Invalid;
                    }

                    var assetType = GameDatabase.DataDirectory.AssetDirectory.GetAssetType(assetTypeName);
                    if (assetType == null)
                    {
                        return AssetId.Invalid;
                    }

                    var assetId = assetType.FindAssetByName(assetName, DataFileSearchFlags.CaseInsensitive);
                    if (assetId == AssetId.Invalid)
                    {
                    }
                    return assetId;
                }
                else
                {
                    return AssetId.Invalid;
                }
            }

            if (targetType.IsEnum && rawValue is string enumString)
            {
                if (Enum.TryParse(targetType, enumString, true, out object enumValue))
                    return enumValue;

                var validValues = string.Join(", ", Enum.GetNames(targetType));
                throw new InvalidOperationException(
                    $"Invalid enum value '{enumString}' for type '{targetType.Name}'. Valid values are: {validValues}");
            }

            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(rawValue.GetType()))
                return converter.ConvertFrom(rawValue);

            return Convert.ChangeType(rawValue, targetType);
        }
    }
}