using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Prototypes;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using MHServerEmu.Games.GameData.Calligraphy;
using System.Collections.Generic;
using System.Linq;
using System;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

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
                return Logger.WarnReturn(false, "LoadPatchDataFromDisk(): Game data directory not found");

            int count = 0;
            var options = new JsonSerializerOptions { Converters = { new PatchEntryConverter() } };

            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, "PatchData", "json"))
            {
                string fileName = Path.GetFileName(filePath);
                try
                {
                    PrototypePatchEntry[] updateValues = FileHelper.DeserializeJson<PrototypePatchEntry[]>(filePath, options);
                    if (updateValues == null) continue;

                    foreach (PrototypePatchEntry value in updateValues)
                    {
                        if (!value.Enabled) continue;
                        PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(value.Prototype);
                        if (prototypeId == PrototypeId.Invalid) continue;

                        AddPatchValue(prototypeId, value);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"Error loading patch file {fileName}");
                }
            }
            return Logger.InfoReturn(true, $"Loaded {count} patch entries from disk.");
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
                    Logger.Debug($"[PreCheck] Pushing {protoRef} ({GameDatabase.GetPrototypeName(protoRef)}) to stack. Stack size will be {_protoStack.Count + 1}.");
                    _protoStack.Push(protoRef);
                    return true;
                }
            }
            return false;
        }

        public void PostOverride(Prototype prototype)
        {
            Logger.Trace($"[PostOverride] Entered for prototype {prototype?.DataRef}. Stack size: {_protoStack.Count}.");
            if (_protoStack.Count == 0) return;

            string currentPath = string.Empty;
            if (prototype.DataRef == PrototypeId.Invalid && !_pathDict.TryGetValue(prototype, out currentPath)) return;

            PrototypeId patchProtoRef = _protoStack.Peek();
            if (prototype.DataRef != PrototypeId.Invalid)
            {
                if (prototype.DataRef != patchProtoRef) return;
                if (_patchDict.ContainsKey(prototype.DataRef))
                {
                    patchProtoRef = _protoStack.Pop();
                }
            }

            if (!_patchDict.TryGetValue(patchProtoRef, out var list)) return;

            foreach (var entry in list)
            {
                if (!entry.Patched)
                {
                    // We will modify CheckAndUpdate next
                    CheckAndUpdate(entry, prototype);
                }
            }

            if (_protoStack.Count == 0)
            {
                Logger.Debug("[PostOverride] Stack is now empty. Clearing Path Dictionary.");
                _pathDict.Clear();
            }
        }

        private bool CheckAndUpdate(PrototypePatchEntry entry, Prototype prototype)
        {
            var targetObject = GetObjectFromPath(prototype, entry.СlearPath);
            if (targetObject == null)
            {
                // This log is expected if the object path is invalid, so we use Trace level
                Logger.Trace($"[CheckAndUpdate] Target object not found for path '{entry.СlearPath}'. Skipping patch '{entry.Path}'.");
                return false;
            }

            var fieldInfo = targetObject.GetType().GetProperty(entry.FieldName);
            if (fieldInfo == null)
            {
                // This is more severe, likely a config error, so we use Warn level
                Logger.Warn($"[CheckAndUpdate] Field '{entry.FieldName}' not found on target '{targetObject.GetType().Name}' for patch '{entry.Path}'");
                return false;
            }

            UpdateValue(targetObject, fieldInfo, entry);
            return true;
        }

        private void UpdateValue(object targetObject, System.Reflection.PropertyInfo fieldInfo, PrototypePatchEntry entry)
        {
            try
            {
                if (entry.ArrayValue)
                {
                    if (entry.ArrayIndex != -1)
                        SetIndexValue(targetObject, fieldInfo, entry.ArrayIndex, entry.Value);
                    else
                        InsertValue(targetObject, fieldInfo, entry.Value);
                }
                else
                {
                    fieldInfo.SetValue(targetObject, ConvertValue(entry.Value.GetValue(), fieldInfo.PropertyType));
                }
                entry.Patched = true;
            }
            catch (Exception ex)
            {
                Logger.ErrorException(ex, $"Failed UpdateValue: [{entry.Prototype}] [{entry.Path}]");
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
            _pathDict[child] = string.IsNullOrEmpty(parentPath) ? fieldName : $"{parentPath}.{fieldName}";
        }

        public void SetPathIndex(Prototype parent, Prototype child, string fieldName, int index)
        {
            if (child == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
            {
                parentPath = string.Empty;
            }
            _pathDict[child] = string.IsNullOrEmpty(parentPath) ? $"{fieldName}[{index}]" : $"{parentPath}.{fieldName}[{index}]";
        }

        private static void SetIndexValue(object target, System.Reflection.PropertyInfo fieldInfo, int index, ValueBase value)
        {
            if (fieldInfo.GetValue(target) is not Array array || index < 0 || index >= array.Length) return;
            Type elementType = fieldInfo.PropertyType.GetElementType();
            if (elementType == null) return;

            object valueEntry = value.GetValue();
            object finalValue;

            
            if (typeof(Prototype).IsAssignableFrom(elementType) && (valueEntry is PrototypeId || valueEntry is ulong || value.ValueType == ValueType.PrototypeDataRef))
            {
                var dataRef = (PrototypeId)Convert.ChangeType(valueEntry, typeof(ulong));
                finalValue = GameDatabase.GetPrototype<Prototype>(dataRef);

                if (finalValue == null)
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype or could not be found.");
            }
            else
            {
               
                finalValue = ConvertValue(valueEntry, elementType);
            }

           
            if (!elementType.IsAssignableFrom(finalValue.GetType()))
                throw new InvalidOperationException($"The resolved value of type {finalValue.GetType().Name} cannot be assigned to an array element of type {elementType.Name}.");

            array.SetValue(finalValue, index);
        }

        private static void InsertValue(object target, System.Reflection.PropertyInfo fieldInfo, ValueBase value)
        {
            if (!fieldInfo.PropertyType.IsArray)
                throw new InvalidOperationException($"Field {fieldInfo.Name} is not an array.");

            Type elementType = fieldInfo.PropertyType.GetElementType();
            if (elementType == null)
                throw new InvalidOperationException($"Could not determine element type for array {fieldInfo.Name}.");

            var valueEntry = value.GetValue();
            var currentArray = (Array)fieldInfo.GetValue(target);
            var valuesToAdd = valueEntry as Array;

            if (valuesToAdd != null)
            {
                // This is the new logic for inserting an array of items
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + valuesToAdd.Length);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                for (int i = 0; i < valuesToAdd.Length; i++)
                {
                    object element = GetElementValue(valuesToAdd.GetValue(i), elementType);
                    newArray.SetValue(element, (currentArray?.Length ?? 0) + i);
                }
                fieldInfo.SetValue(target, newArray);
            }
            else
            {
                // This is the original logic for inserting a single item
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + 1);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                newArray.SetValue(GetElementValue(valueEntry, elementType), newArray.Length - 1);
                fieldInfo.SetValue(target, newArray);
            }
        }

        private static bool IsTypeCompatible(Type baseType, object rawValue, ValueType valueType)
        {
            if (rawValue is Array rawArray)
            {
                var elementValueType = valueType.ToString().EndsWith("Array") ? (ValueType)Enum.Parse(typeof(ValueType), valueType.ToString().Replace("Array", "")) : valueType;
                return rawArray.Cast<object>().All(element => IsTypeCompatible(baseType, element, elementValueType));
            }
            if (typeof(Prototype).IsAssignableFrom(baseType))
            {
                if (rawValue is PrototypeId dataRef)
                {
                    var actualPrototype = GameDatabase.GetPrototype<Prototype>(dataRef);
                    return actualPrototype != null && baseType.IsAssignableFrom(actualPrototype.GetType());
                }
                if (rawValue is JsonElement jsonElement && (valueType == ValueType.Prototype || valueType == ValueType.ComplexObject))
                {
                    if (jsonElement.TryGetProperty("ParentDataRef", out var parentDataRefElement) && parentDataRefElement.TryGetUInt64(out var id))
                    {
                        Type classType = GameDatabase.DataDirectory.GetPrototypeClassType((PrototypeId)id);
                        return classType != null && baseType.IsAssignableFrom(classType);
                    }
                    return false;
                }
            }
            if (valueType == ValueType.ComplexObject && rawValue is JsonElement)
            {
                return true;
            }
            return baseType.IsAssignableFrom(rawValue.GetType());
        }
        private object GetObjectFromPath(object root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return root;
            }

            object current = root;
            // Use a regex to correctly split on dots but not dots inside brackets (future-proofing)
            var pathParts = path.Split('.');

            foreach (var part in pathParts)
            {
                if (current == null) return null;

                var arrayMatch = System.Text.RegularExpressions.Regex.Match(part, @"(\w+)\[(\d+)\]");
                try
                {
                    if (arrayMatch.Success)
                    {

                        var propertyName = arrayMatch.Groups[1].Value;
                        var index = int.Parse(arrayMatch.Groups[2].Value);

                        var propInfo = current.GetType().GetProperty(propertyName);
                        if (propInfo == null) return null;

                        if (propInfo.GetValue(current) is not System.Collections.IList list) return null;
                        if (index >= list.Count) return null; // Index out of bounds

                        current = list[index];
                    }
                    else
                    {

                        var propInfo = current.GetType().GetProperty(part);
                        if (propInfo == null) return null;
                        current = propInfo.GetValue(current);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WarnException(ex, $"Failed to navigate path part '{part}' in full path '{path}'.");
                    return null;
                }
            }
            return current;
        }
        private static object GetElementValue(object valueEntry, Type elementType)
        {
            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is PrototypeId dataRef)
                return GameDatabase.GetPrototype<Prototype>(dataRef) ?? throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype.");
            return ConvertValue(valueEntry, elementType);
        }

        public static object ConvertValue(object rawValue, Type targetType)
        {
            if (rawValue == null || (rawValue is JsonElement jsonValCheck && jsonValCheck.ValueKind == JsonValueKind.Null)) return null;
            if (targetType.IsInstanceOfType(rawValue)) return rawValue;

            if (targetType == typeof(PropertyId) && rawValue is JsonElement jsonValForPropId && jsonValForPropId.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    if (jsonValForPropId.TryGetProperty("PropertyEnum", out var propEnumElement))
                    {
                        var propertyEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), propEnumElement.GetString(), true);

                        // Check for parameters
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
                    Logger.Warn($"Failed to parse PropertyId from JSON: {ex.Message}");
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

                
                if (jsonVal.ValueKind == JsonValueKind.Object && jsonVal.TryGetProperty("ParentDataRef", out _))
                {
                    return PatchEntryConverter.ParseJsonPrototype(jsonVal);
                }
                if (typeof(EvalPrototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                    {
                        return PatchEntryConverter.ParseJsonPrototype(jsonVal) as EvalPrototype;
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

                            // Check for parameters
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
                        Logger.Warn($"Failed to parse PropertyId from JSON: {ex.Message}");
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
                        return jsonVal.ValueKind == JsonValueKind.String
                            ? Enum.Parse(targetType, jsonVal.GetString(), true)
                            : Enum.ToObject(targetType, jsonVal.GetInt32());
                    }
                }
                catch (Exception) { /* Fall through to final conversion attempts */ }
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

            if(targetType == typeof(AssetId) && rawValue is string assetString)
{
                int typeNameStart = assetString.LastIndexOf('(');
                int typeNameEnd = assetString.LastIndexOf(')');

                if (typeNameStart != -1 && typeNameEnd > typeNameStart)
                {
                    string assetName = assetString.Substring(0, typeNameStart).Trim();
                    string assetTypeName = assetString.Substring(typeNameStart + 1, typeNameEnd - typeNameStart - 1).Trim();

                    if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(assetTypeName))
                    {
                        Logger.Warn($"Invalid AssetId format: empty name or type in '{assetString}'");
                        return AssetId.Invalid;
                    }

                    var assetType = GameDatabase.DataDirectory.AssetDirectory.GetAssetType(assetTypeName);
                    if (assetType == null)
                    {
                        Logger.Warn($"Asset type '{assetTypeName}' not found for asset '{assetName}'");
                        return AssetId.Invalid;
                    }

                    var assetId = assetType.FindAssetByName(assetName, DataFileSearchFlags.CaseInsensitive);
                    if (assetId == AssetId.Invalid)
                    {
                        Logger.Warn($"Asset '{assetName}' of type '{assetTypeName}' not found");
                    }
                    return assetId;
                }
                else
                {
                    Logger.Warn($"AssetId string '{assetString}' does not match expected format 'AssetName (AssetType)'");
                    return AssetId.Invalid;
                }
            }

            if (targetType.IsEnum && rawValue is string enumString)
                return Enum.Parse(targetType, enumString, true);

            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(rawValue.GetType()))
                return converter.ConvertFrom(rawValue);

            return Convert.ChangeType(rawValue, targetType);
        }
    }
}