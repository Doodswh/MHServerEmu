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
            int skippedDisabled = 0;
            int skippedInvalid = 0;
            var options = new JsonSerializerOptions { Converters = { new PatchEntryConverter() } };

            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, "PatchData", "json"))
            {
                string fileName = Path.GetFileName(filePath);
                Logger.Info($"Loading patch file: {fileName}");

                try
                {
                    PrototypePatchEntry[] updateValues = FileHelper.DeserializeJson<PrototypePatchEntry[]>(filePath, options);
                    if (updateValues == null)
                    {
                        Logger.Warn($"Failed to deserialize patch file: {fileName}");
                        continue;
                    }

                    foreach (PrototypePatchEntry value in updateValues)
                    {
                        if (!value.Enabled)
                        {
                            skippedDisabled++;
                            Logger.Trace($"Skipped disabled patch: {value.Prototype} -> {value.Path}");
                            continue;
                        }

                        PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(value.Prototype);
                        if (prototypeId == PrototypeId.Invalid)
                        {
                            skippedInvalid++;
                            Logger.Warn($"Invalid prototype reference in patch: '{value.Prototype}' (Path: {value.Path})");
                            continue;
                        }

                        AddPatchValue(prototypeId, value);
                        count++;
                        Logger.Debug($"Loaded patch: {value.Prototype} -> {value.Path} (ValueType: {value.Value.ValueType})");
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"Error loading patch file {fileName}");
                }
            }

            Logger.Info($"Patch loading summary: {count} loaded, {skippedDisabled} disabled, {skippedInvalid} invalid");
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
                            Logger.Debug($"Applied Properties patch: {GameDatabase.GetPrototypeName(protoRef)} -> {entry.Path}");
                            return true;
                        }
                        else
                        {
                            Logger.Warn($"Properties patch value is null: {GameDatabase.GetPrototypeName(protoRef)} -> {entry.Path}");
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
            if (prototype.DataRef == PrototypeId.Invalid && !_pathDict.TryGetValue(prototype, out currentPath))
            {
                Logger.Trace($"[PostOverride] No path found for nested prototype of type {prototype?.GetType().Name}");
                return;
            }

            PrototypeId patchProtoRef = _protoStack.Peek();
            if (prototype.DataRef != PrototypeId.Invalid)
            {
                if (prototype.DataRef != patchProtoRef)
                {
                    Logger.Trace($"[PostOverride] Prototype DataRef {prototype.DataRef} doesn't match stack top {patchProtoRef}");
                    return;
                }
                if (_patchDict.ContainsKey(prototype.DataRef))
                {
                    patchProtoRef = _protoStack.Pop();
                    Logger.Debug($"[PostOverride] Popped {patchProtoRef} from stack. Stack size now: {_protoStack.Count}");
                }
            }

            if (!_patchDict.TryGetValue(patchProtoRef, out var list))
            {
                Logger.Trace($"[PostOverride] No patches found for {patchProtoRef}");
                return;
            }

            int appliedCount = 0;
            foreach (var entry in list)
            {
                if (!entry.Patched)
                {
                    Logger.Debug($"[PostOverride] Attempting patch: {entry.Path} on {GameDatabase.GetPrototypeName(patchProtoRef)}");
                    if (CheckAndUpdate(entry, prototype))
                    {
                        appliedCount++;
                        Logger.Info($"✓ Successfully applied patch: {GameDatabase.GetPrototypeName(patchProtoRef)} -> {entry.Path}");
                    }
                    else
                    {
                        Logger.Warn($"✗ Failed to apply patch: {GameDatabase.GetPrototypeName(patchProtoRef)} -> {entry.Path}");
                    }
                }
            }

            if (appliedCount > 0)
                Logger.Debug($"[PostOverride] Applied {appliedCount} patches to {GameDatabase.GetPrototypeName(patchProtoRef)}");

            if (_protoStack.Count == 0)
            {
                Logger.Debug("[PostOverride] Stack is now empty. Clearing Path Dictionary.");
                _pathDict.Clear();
            }
        }

        private bool CheckAndUpdate(PrototypePatchEntry entry, Prototype prototype)
        {
            Logger.Trace($"[CheckAndUpdate] Entry: Prototype={entry.Prototype}, Path={entry.Path}, ClearPath={entry.СlearPath}, FieldName={entry.FieldName}, ArrayValue={entry.ArrayValue}, ArrayIndex={entry.ArrayIndex}");

            var targetObject = GetObjectFromPath(prototype, entry.СlearPath);
            if (targetObject == null)
            {
                Logger.Warn($"[CheckAndUpdate] FAILED: Target object not found for path '{entry.СlearPath}'. Full path: '{entry.Path}'");
                Logger.Warn($"  Available paths from prototype root: {string.Join(", ", GetAvailableProperties(prototype))}");
                return false;
            }

            Logger.Trace($"[CheckAndUpdate] Found target object of type: {targetObject.GetType().Name}");

            var fieldInfo = targetObject.GetType().GetProperty(entry.FieldName);
            if (fieldInfo == null)
            {
                Logger.Warn($"[CheckAndUpdate] FAILED: Field '{entry.FieldName}' not found on target type '{targetObject.GetType().Name}'");
                Logger.Warn($"  Available properties: {string.Join(", ", GetAvailableProperties(targetObject))}");
                return false;
            }

            Logger.Trace($"[CheckAndUpdate] Found field '{entry.FieldName}' of type: {fieldInfo.PropertyType.Name}");
            Logger.Trace($"[CheckAndUpdate] Value to apply: {entry.Value.GetValue()} (ValueType: {entry.Value.ValueType})");

            UpdateValue(targetObject, fieldInfo, entry);
            return true;
        }

        private List<string> GetAvailableProperties(object obj)
        {
            if (obj == null) return new List<string> { "null object" };

            return obj.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                .ToList();
        }

        private void UpdateValue(object targetObject, System.Reflection.PropertyInfo fieldInfo, PrototypePatchEntry entry)
        {
            try
            {
                if (entry.ArrayValue)
                {
                    Logger.Debug($"[UpdateValue] Array operation: ArrayIndex={entry.ArrayIndex}");
                    if (entry.ArrayIndex != -1)
                    {
                        Logger.Debug($"[UpdateValue] Setting array index {entry.ArrayIndex}");
                        SetIndexValue(targetObject, fieldInfo, entry.ArrayIndex, entry.Value);
                    }
                    else
                    {
                        Logger.Debug($"[UpdateValue] Inserting value into array");
                        InsertValue(targetObject, fieldInfo, entry.Value);
                    }
                }
                else
                {
                    object rawValue = entry.Value.GetValue();
                    Logger.Debug($"[UpdateValue] Converting value. Raw type: {rawValue?.GetType().Name ?? "null"}, Target type: {fieldInfo.PropertyType.Name}");

                    object convertedValue = ConvertValue(rawValue, fieldInfo.PropertyType);

                    // Special handling for Eval and ComplexObject - they're already parsed
                    if (entry.Value.ValueType == ValueType.Eval || entry.Value.ValueType == ValueType.ComplexObject)
                    {
                        Logger.Debug($"[UpdateValue] Setting pre-parsed {entry.Value.ValueType} value");
                        fieldInfo.SetValue(targetObject, convertedValue);
                    }
                    else
                    {
                        Logger.Debug($"[UpdateValue] Setting converted value of type: {convertedValue?.GetType().Name ?? "null"}");
                        fieldInfo.SetValue(targetObject, convertedValue);
                    }
                }
                entry.Patched = true;
                Logger.Debug($"[UpdateValue] SUCCESS: Marked entry as patched");
            }
            catch (Exception ex)
            {
                Logger.ErrorException(ex, $"[UpdateValue] FAILED: [{entry.Prototype}] [{entry.Path}]");
                Logger.Error($"  Target object type: {targetObject.GetType().Name}");
                Logger.Error($"  Field name: {fieldInfo.Name}");
                Logger.Error($"  Field type: {fieldInfo.PropertyType.Name}");
                Logger.Error($"  Value type: {entry.Value.ValueType}");
                Logger.Error($"  Raw value: {entry.Value.GetValue()}");
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
            Logger.Trace($"[SetPath] Registered path: {newPath} for child type {child.GetType().Name}");
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
            Logger.Trace($"[SetPathIndex] Registered indexed path: {newPath} for child type {child.GetType().Name}");
        }

        private static void SetIndexValue(object target, System.Reflection.PropertyInfo fieldInfo, int index, ValueBase value)
        {
            if (fieldInfo.GetValue(target) is not Array array)
            {
                Logger.Warn($"[SetIndexValue] Field '{fieldInfo.Name}' is not an array");
                return;
            }

            if (index < 0 || index >= array.Length)
            {
                Logger.Warn($"[SetIndexValue] Index {index} out of bounds for array of length {array.Length}");
                return;
            }

            Type elementType = fieldInfo.PropertyType.GetElementType();
            if (elementType == null)
            {
                Logger.Warn($"[SetIndexValue] Could not determine element type for array field '{fieldInfo.Name}'");
                return;
            }

            object valueEntry = value.GetValue();
            object finalValue;

            Logger.Debug($"[SetIndexValue] Setting index {index} in array of type {elementType.Name}");

            if (typeof(Prototype).IsAssignableFrom(elementType) && (valueEntry is PrototypeId || valueEntry is ulong || value.ValueType == ValueType.PrototypeDataRef))
            {
                var dataRef = (PrototypeId)Convert.ChangeType(valueEntry, typeof(ulong));
                Logger.Debug($"[SetIndexValue] Resolving PrototypeId: {dataRef}");
                finalValue = GameDatabase.GetPrototype<Prototype>(dataRef);

                if (finalValue == null)
                {
                    Logger.Error($"[SetIndexValue] FAILED: DataRef {dataRef} could not be resolved to a Prototype");
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype or could not be found.");
                }
            }
            else if (typeof(EvalPrototype).IsAssignableFrom(elementType) && valueEntry is EvalPrototype)
            {
                Logger.Debug($"[SetIndexValue] Using pre-parsed EvalPrototype");
                finalValue = valueEntry;
            }
            else if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is Prototype)
            {
                Logger.Debug($"[SetIndexValue] Using pre-parsed Prototype");
                finalValue = valueEntry;
            }
            else
            {
                Logger.Debug($"[SetIndexValue] Converting value to {elementType.Name}");
                finalValue = ConvertValue(valueEntry, elementType);
            }

            if (!elementType.IsAssignableFrom(finalValue.GetType()))
            {
                Logger.Error($"[SetIndexValue] Type mismatch: Cannot assign {finalValue.GetType().Name} to {elementType.Name}");
                throw new InvalidOperationException($"The resolved value of type {finalValue.GetType().Name} cannot be assigned to an array element of type {elementType.Name}.");
            }

            array.SetValue(finalValue, index);
            Logger.Debug($"[SetIndexValue] Successfully set array index {index}");
        }

        private static void InsertValue(object target, System.Reflection.PropertyInfo fieldInfo, ValueBase value)
        {
            Logger.Debug($"[InsertValue] Inserting into field '{fieldInfo.Name}'");

            if (!fieldInfo.PropertyType.IsArray)
                throw new InvalidOperationException($"Field {fieldInfo.Name} is not an array.");

            Type elementType = fieldInfo.PropertyType.GetElementType();
            if (elementType == null)
                throw new InvalidOperationException($"Could not determine element type for array {fieldInfo.Name}.");

            var valueEntry = value.GetValue();
            var currentArray = (Array)fieldInfo.GetValue(target);
            var valuesToAdd = valueEntry as Array;

            Logger.Debug($"[InsertValue] Current array length: {currentArray?.Length ?? 0}, Element type: {elementType.Name}");

            if (valuesToAdd != null)
            {
                Logger.Debug($"[InsertValue] Inserting {valuesToAdd.Length} elements");
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + valuesToAdd.Length);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                for (int i = 0; i < valuesToAdd.Length; i++)
                {
                    object element = GetElementValue(valuesToAdd.GetValue(i), elementType);
                    newArray.SetValue(element, (currentArray?.Length ?? 0) + i);
                    Logger.Trace($"[InsertValue] Added element {i}: {element?.GetType().Name ?? "null"}");
                }
                fieldInfo.SetValue(target, newArray);
                Logger.Debug($"[InsertValue] Successfully inserted array of {valuesToAdd.Length} elements");
            }
            else
            {
                Logger.Debug($"[InsertValue] Inserting single element");
                var newArray = Array.CreateInstance(elementType, (currentArray?.Length ?? 0) + 1);
                if (currentArray != null)
                    Array.Copy(currentArray, newArray, currentArray.Length);

                object element = GetElementValue(valueEntry, elementType);
                newArray.SetValue(element, newArray.Length - 1);
                fieldInfo.SetValue(target, newArray);
                Logger.Debug($"[InsertValue] Successfully inserted single element of type {element?.GetType().Name ?? "null"}");
            }
        }

        private object GetObjectFromPath(object root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Logger.Trace($"[GetObjectFromPath] Empty path, returning root of type {root?.GetType().Name}");
                return root;
            }

            Logger.Trace($"[GetObjectFromPath] Navigating path: '{path}' from root type {root?.GetType().Name}");

            object current = root;
            var pathParts = path.Split('.');

            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (current == null)
                {
                    Logger.Warn($"[GetObjectFromPath] Current object is null at path part {i}: '{part}'");
                    return null;
                }

                var arrayMatch = System.Text.RegularExpressions.Regex.Match(part, @"(\w+)\[(\d+)\]");
                try
                {
                    if (arrayMatch.Success)
                    {
                        var propertyName = arrayMatch.Groups[1].Value;
                        var index = int.Parse(arrayMatch.Groups[2].Value);

                        Logger.Trace($"[GetObjectFromPath] Array access: {propertyName}[{index}]");

                        var propInfo = current.GetType().GetProperty(propertyName);
                        if (propInfo == null)
                        {
                            Logger.Warn($"[GetObjectFromPath] Property '{propertyName}' not found on type {current.GetType().Name}");
                            return null;
                        }

                        if (propInfo.GetValue(current) is not System.Collections.IList list)
                        {
                            Logger.Warn($"[GetObjectFromPath] Property '{propertyName}' is not a list");
                            return null;
                        }

                        if (index >= list.Count)
                        {
                            Logger.Warn($"[GetObjectFromPath] Index {index} out of bounds for list of size {list.Count}");
                            return null;
                        }

                        current = list[index];
                        Logger.Trace($"[GetObjectFromPath] Navigated to array element of type {current?.GetType().Name}");
                    }
                    else
                    {
                        Logger.Trace($"[GetObjectFromPath] Property access: {part}");

                        var propInfo = current.GetType().GetProperty(part);
                        if (propInfo == null)
                        {
                            Logger.Warn($"[GetObjectFromPath] Property '{part}' not found on type {current.GetType().Name}");
                            Logger.Warn($"  Available: {string.Join(", ", GetAvailableProperties(current))}");
                            return null;
                        }

                        current = propInfo.GetValue(current);
                        Logger.Trace($"[GetObjectFromPath] Navigated to property of type {current?.GetType().Name}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WarnException(ex, $"[GetObjectFromPath] Failed at path part '{part}' (index {i}) in full path '{path}'");
                    return null;
                }
            }

            Logger.Trace($"[GetObjectFromPath] Successfully navigated to object of type {current?.GetType().Name}");
            return current;
        }

        private static object GetElementValue(object valueEntry, Type elementType)
        {
            Logger.Trace($"[GetElementValue] Converting {valueEntry?.GetType().Name ?? "null"} to {elementType.Name}");

            // Handle already-parsed prototypes
            if (typeof(EvalPrototype).IsAssignableFrom(elementType) && valueEntry is EvalPrototype)
            {
                Logger.Trace($"[GetElementValue] Using pre-parsed EvalPrototype");
                return valueEntry;
            }

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is Prototype)
            {
                Logger.Trace($"[GetElementValue] Using pre-parsed Prototype");
                return valueEntry;
            }

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is PrototypeId dataRef)
            {
                Logger.Debug($"[GetElementValue] Resolving PrototypeId {dataRef} to Prototype");
                var result = GameDatabase.GetPrototype<Prototype>(dataRef);
                if (result == null)
                {
                    Logger.Error($"[GetElementValue] FAILED: Could not resolve PrototypeId {dataRef}");
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype.");
                }
                return result;
            }

            return ConvertValue(valueEntry, elementType);
        }

        public static object ConvertValue(object rawValue, Type targetType)
        {
            Logger.Trace($"[ConvertValue] Converting {rawValue?.GetType().Name ?? "null"} to {targetType.Name}");

            if (rawValue == null || (rawValue is JsonElement jsonValCheck && jsonValCheck.ValueKind == JsonValueKind.Null))
            {
                Logger.Trace($"[ConvertValue] Raw value is null, returning null");
                return null;
            }

            if (targetType.IsInstanceOfType(rawValue))
            {
                Logger.Trace($"[ConvertValue] Value is already correct type, returning as-is");
                return rawValue;
            }

            // Handle already-parsed EvalPrototype
            if (typeof(EvalPrototype).IsAssignableFrom(targetType) && rawValue is EvalPrototype evalProto)
            {
                Logger.Trace($"[ConvertValue] Using pre-parsed EvalPrototype");
                return evalProto;
            }

            // Handle already-parsed Prototype (ComplexObject)
            if (typeof(Prototype).IsAssignableFrom(targetType) && rawValue is Prototype proto)
            {
                Logger.Trace($"[ConvertValue] Using pre-parsed Prototype");
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

                        Logger.Debug($"[ConvertValue] Parsed PropertyId: {propertyEnum}");
                        return new PropertyId(propertyEnum, parameters[0], parameters[1], parameters[2], parameters[3]);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WarnException(ex, $"[ConvertValue] Failed to parse PropertyId from JSON");
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
                    Logger.Debug($"[ConvertValue] Converting JSON array of {jsonArray.Length} elements to {elementType.Name}[]");

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
                        Logger.Debug($"[ConvertValue] Parsing EvalPrototype from JSON");
                        return PatchEntryConverter.ParseJsonEval(jsonVal);
                    }
                }

                // Handle Prototype from JSON
                if (typeof(Prototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                    {
                        Logger.Debug($"[ConvertValue] Parsing Prototype from JSON");
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

                            Logger.Debug($"[ConvertValue] Parsed PropertyId: {propertyEnum}");
                            return new PropertyId(propertyEnum, parameters[0], parameters[1], parameters[2], parameters[3]);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WarnException(ex, $"[ConvertValue] Failed to parse PropertyId from JSON");
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
                    Logger.WarnException(ex, $"[ConvertValue] Failed to convert JsonElement to {targetType.Name}");
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