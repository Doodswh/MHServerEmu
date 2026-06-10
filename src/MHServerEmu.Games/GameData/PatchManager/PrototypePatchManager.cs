using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;

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
                return false;

            int count = 0;
            int skippedDisabled = 0;
            int skippedInvalid = 0;
            int resolvedViaReplacement = 0;
            var options = new JsonSerializerOptions { Converters = { new PatchEntryConverter() } };

            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, "PatchData", "json"))
            {
                string fileName = Path.GetFileName(filePath);

                try
                {
                    string jsonText = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;

                    bool isOCPatch = false;
                    JsonElement ocArrayToLoad = default;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Patches", out var patchesArray))
                    {
                        isOCPatch = true;
                        ocArrayToLoad = patchesArray;
                    }
                    else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        var firstElement = root.EnumerateArray().First();
                        if (firstElement.ValueKind == JsonValueKind.Object && firstElement.TryGetProperty("Steps", out _))
                        {
                            isOCPatch = true;
                            ocArrayToLoad = root;
                        }
                    }

                    if (isOCPatch)
                    {
                        // Route to the OC Adapter
                        LoadOpenCalligraphyPatch(ocArrayToLoad, fileName, ref count, ref skippedDisabled, ref skippedInvalid, ref resolvedViaReplacement);
                        continue; // Skip the legacy deserializer
                    }

                    PrototypePatchEntry[] updateValues = FileHelper.DeserializeJson<PrototypePatchEntry[]>(filePath, options);
                    if (updateValues == null)
                        continue;

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
                            prototypeId = TryResolveViaReplacementDirectory(value.Prototype, out string replacedName);
                            if (prototypeId != PrototypeId.Invalid)
                            {
                                Logger.Warn($"[PatchManager] Prototype '{value.Prototype}' was not found by name but was resolved to '{replacedName}' via ReplacementDirectory.");
                                resolvedViaReplacement++;
                            }
                        }

                        if (prototypeId == PrototypeId.Invalid)
                        {
                            Logger.Warn($"[PatchManager] Could not resolve prototype '{value.Prototype}' in '{fileName}'. Entry skipped.");
                            skippedInvalid++;
                            continue;
                        }

                        AddPatchValue(prototypeId, value);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[PatchManager] Failed to load patch file '{fileName}': {ex.Message}");
                }
            }

            Logger.Info($"[PatchManager] Loaded {count} patch entries from '{patchDirectory}' " +
                        $"({skippedDisabled} disabled, {skippedInvalid} invalid, {resolvedViaReplacement} resolved via ReplacementDirectory).");
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

        // Iterate all Properties-typed entries rather than returning on the first hit,
        // so multiple Properties patches targeting the same prototype are all applied.
        public bool CheckProperties(PrototypeId protoRef, out PropertyCollection prop)
        {
            prop = null;
            if (!_initialized) return false;

            if (!_patchDict.TryGetValue(protoRef, out var list))
                return false;

            bool anyApplied = false;
            foreach (var entry in list)
            {
                if (entry.Value.ValueType != ValueType.Properties)
                    continue;

                var patchProp = entry.Value.GetValue() as PropertyCollection;
                if (patchProp == null)
                    continue;

                if (prop == null)
                    prop = new PropertyCollection();

                foreach (var kvp in patchProp)
                    prop.SetProperty(kvp.Value, kvp.Key);

                entry.Patched = true;
                anyApplied = true;
            }

            return anyApplied;
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
                return;

            PrototypeId patchProtoRef = _protoStack.Peek();
            if (prototype.DataRef != PrototypeId.Invalid)
            {
                if (prototype.DataRef != patchProtoRef)
                    return;

                if (_patchDict.ContainsKey(prototype.DataRef))
                    patchProtoRef = _protoStack.Pop();
            }

            if (!_patchDict.TryGetValue(patchProtoRef, out var list))
                return;

            int appliedCount = 0;
            foreach (var entry in list)
            {
                if (!entry.Patched)
                {
                    if (CheckAndUpdate(entry, prototype))
                        appliedCount++;
                    else
                        Logger.Warn($"[PatchManager] Failed to apply patch for field '{entry.FieldName}' on '{entry.Prototype}'.");
                }
            }

            // The dangling if had no body — log when patches are applied.
            if (appliedCount > 0)
                Logger.Trace($"[PatchManager] Applied {appliedCount} patch(es) to '{entry_PrototypeName(patchProtoRef)}'.");

            if (_protoStack.Count == 0)
                _pathDict.Clear();
        }

        
        private static PrototypeId TryResolveViaReplacementDirectory(string prototypeName, out string replacedName)
        {
            replacedName = null;

            var record = ReplacementDirectory.Instance.FindRecordByName(prototypeName);
            if (record == null)
                return PrototypeId.Invalid;

            PrototypeId resolved = GameDatabase.GetPrototypeRefByName(record.Name);
            if (resolved != PrototypeId.Invalid)
                replacedName = record.Name;

            return resolved;
        }

        private string entry_PrototypeName(PrototypeId protoRef)
        {
            return GameDatabase.GetPrototypeName(protoRef) ?? protoRef.ToString();
        }

        private bool CheckAndUpdate(PrototypePatchEntry entry, Prototype prototype)
        {
            // Navigate to the target object using the path. We pass the FieldName so the solver can find mixins dynamically.
            var targetObject = GetOrCreateObjectFromPath(prototype, entry.СlearPath, entry.FieldName);
            if (targetObject == null)
            {
                Logger.Warn($"[PatchManager] Failed to resolve path '{entry.СlearPath}' on prototype '{entry.Prototype}'. Double check your spelling or array indexes.");
                return false;
            }

            // Use the enhanced field lookup from PrototypeClassManager
            var enhancedField = GameDatabase.PrototypeClassManager.GetFieldByName(targetObject.GetType(), entry.FieldName);
            if (!enhancedField.HasValue)
            {
                var availableProps = GetAvailableProperties(targetObject);
                Logger.Warn($"[PatchManager] ❌ Field/Property '{entry.FieldName}' NOT FOUND on type '{targetObject.GetType().Name}'.");
                Logger.Warn($"[PatchManager] Path: '{entry.Path}' | Prototype: '{entry.Prototype}'");
                Logger.Warn($"[PatchManager] ✅ Valid Fields/Properties for this object are: {string.Join(", ", availableProps)}");
                return false;
            }

            var fieldInfo = enhancedField.Value;
            if (entry.Value.ValueType == ValueType.Properties && fieldInfo.PropertyInfo.PropertyType == typeof(PropertyCollection))
            {
                var targetPropCollection = (PropertyCollection)fieldInfo.PropertyInfo.GetValue(targetObject);
                var patchPropCollection = (PropertyCollection)entry.Value.GetValue();

                if (targetPropCollection != null && patchPropCollection != null)
                {
                    if (entry.ReplaceEntirely) targetPropCollection.Clear();

                    foreach (var kvp in patchPropCollection)
                        targetPropCollection.SetProperty(kvp.Value, kvp.Key);

                    entry.Patched = true;
                    Logger.Trace($"[PatchManager] Successfully patched PropertyCollection on '{entry.Prototype}'.");
                    return true;
                }
                else
                {
                    Logger.Warn($"[PatchManager] PropertyCollection patch failed on '{entry.Prototype}'. Target or Patch collection was null.");
                    return false;
                }
            }

            // Validate field writability
            if (!fieldInfo.CanWrite)
            {
                Logger.Warn($"[PatchManager] ❌ Field/Property '{entry.FieldName}' on '{targetObject.GetType().Name}' is Read-Only! (Prototype: '{entry.Prototype}')");
                return false;
            }

            // Apply the update
            bool success = UpdateValue(targetObject, fieldInfo, entry);
            if (!success)
                Logger.Warn($"[PatchManager] ❌ Failed to update value for '{entry.FieldName}' on '{entry.Prototype}'. See errors above.");

            return success;
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
                        SetIndexValue(targetObject, fieldInfo, entry.ArrayIndex, entry.Value);
                    else
                        InsertValue(targetObject, fieldInfo, entry.Value);
                }
                else
                {
                    object rawValue = entry.Value.GetValue();
                    object convertedValue = ConvertValue(rawValue, fieldInfo.PropertyInfo.PropertyType);

                    if (!GameDatabase.PrototypeClassManager.TrySetPropertyValue(targetObject, fieldInfo.PropertyInfo, convertedValue))
                    {
                        Logger.Warn($"[PatchManager] TrySetPropertyValue rejected the value for '{entry.FieldName}'. Target expects type '{fieldInfo.PropertyInfo.PropertyType.Name}', but got '{convertedValue?.GetType().Name ?? "null"}'.");
                        return false;
                    }
                }

                entry.Patched = true;
                Logger.Trace($"[PatchManager] Successfully patched '{entry.FieldName}' on '{entry.Prototype}'.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[PatchManager] CRITICAL ERROR updating '{entry.FieldName}' on '{entry.Prototype}': {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public void SetPath(Prototype parent, Prototype child, string fieldName)
        {
            if (child == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;
            string newPath = string.IsNullOrEmpty(parentPath) ? fieldName : $"{parentPath}.{fieldName}";
            _pathDict[child] = newPath;
        }

        public void SetPathIndex(Prototype parent, Prototype child, string fieldName, int index)
        {
            if (child == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;
            string newPath = string.IsNullOrEmpty(parentPath) ? $"{fieldName}[{index}]" : $"{parentPath}.{fieldName}[{index}]";
            _pathDict[child] = newPath;
        }

       
        public void SetPathMixin(Prototype parent, PrototypeMixinList mixinList, string fieldName)
        {
            if (mixinList == null) return;
            _pathDict.TryGetValue(parent, out var parentPath);
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;

            for (int i = 0; i < mixinList.Count; i++)
            {
                var item = mixinList[i];
                if (item?.Prototype == null) continue;

                string mixinKey = $"{fieldName}[BlueprintId={(ulong)item.BlueprintId},Copy={item.BlueprintCopyNum}]";
                string newPath = string.IsNullOrEmpty(parentPath) ? mixinKey : $"{parentPath}.{mixinKey}";
                _pathDict[item.Prototype] = newPath;
            }
        }
        private void LoadOpenCalligraphyPatch(JsonElement patchesArray, string fileName, ref int count, ref int skippedDisabled, ref int skippedInvalid, ref int resolvedViaReplacement)
        {
            foreach (var patchElement in patchesArray.EnumerateArray())
            {
                // 1. Safely get PrototypeName
                if (!patchElement.TryGetProperty("PrototypeName", out var protoNameElement))
                    continue;

                string prototypeName = protoNameElement.GetString();
                PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(prototypeName);

                // Name resolution fallback
                if (prototypeId == PrototypeId.Invalid)
                {
                    prototypeId = TryResolveViaReplacementDirectory(prototypeName, out string replacedName);
                    if (prototypeId != PrototypeId.Invalid) resolvedViaReplacement++;
                }

                if (prototypeId == PrototypeId.Invalid)
                {
                    Logger.Warn($"[PatchManager] Could not resolve prototype '{prototypeName}' in OC Patch '{fileName}'. Skipped.");
                    skippedInvalid++;
                    continue;
                }

                //  get Steps
                if (!patchElement.TryGetProperty("Steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
                    continue;

                var steps = stepsElement.EnumerateArray().ToList();
                if (steps.Count == 0) continue;

                var pathBuilder = new System.Text.StringBuilder();
                bool isFirst = true;
                Type protoClassType = GameDatabase.DataDirectory.GetPrototypeClassType(prototypeId);

                foreach (var step in steps)
                {
                    if (!isFirst) pathBuilder.Append(".");

                    // extract step properties (defaulting to 0 if omitted by JSON serializer)
                    string fieldName = step.TryGetProperty("FieldName", out var fieldNameEl) ? fieldNameEl.GetString() : string.Empty;
                    ulong declaringBp = step.TryGetProperty("DeclaringBlueprintId", out var dbpEl) ? dbpEl.GetUInt64() : 0;
                    byte copyNum = step.TryGetProperty("BlueprintCopyNumber", out var bpcEl) ? bpcEl.GetByte() : (byte)0;

                    if (isFirst && protoClassType != null)
                    {
                        var field = GameDatabase.PrototypeClassManager.GetFieldByName(protoClassType, fieldName);
                        if (!field.HasValue && declaringBp > 0)
                        {
                            pathBuilder.Append($"Components[BlueprintId={declaringBp},Copy={copyNum}].");
                        }
                    }

                    pathBuilder.Append(fieldName);

                    if (step.TryGetProperty("ListIndex", out var listIdxElement) && listIdxElement.ValueKind != JsonValueKind.Null)
                    {
                        pathBuilder.Append($"[{listIdxElement.GetInt32()}]");
                    }

                    isFirst = false;
                }

                var lastStep = steps.Last();
                byte baseType = lastStep.TryGetProperty("BaseType", out var btEl) ? btEl.GetByte() : (byte)0;
                byte structType = lastStep.TryGetProperty("StructureType", out var stEl) ? stEl.GetByte() : (byte)0;
                ValueType mappedType = MapCalligraphyType(baseType, structType);

                // extract CurrentValue (handle missing/null payloads)
                JsonElement currentValueElement;
                if (!patchElement.TryGetProperty("CurrentValue", out currentValueElement))
                {
                    // If completely omitted, generate a safe 'null' JsonElement
                    using var doc = JsonDocument.Parse("null");
                    currentValueElement = doc.RootElement.Clone();
                }

                ValueBase valueBase = PatchEntryConverter.GetValueBase(currentValueElement, mappedType);

                var entry = new PrototypePatchEntry(
                    enabled: true,
                    prototype: prototypeName,
                    path: pathBuilder.ToString(),
                    description: $"Imported from OC: {fileName}",
                    value: valueBase,
                    replaceEntirely: structType == 76
                );

                AddPatchValue(prototypeId, entry);
                count++;
            }
        }

        private static ValueType MapCalligraphyType(byte baseType, byte structType)
        {
            bool isList = structType == 76;

            return baseType switch
            {
                66 => isList ? ValueType.BooleanArray : ValueType.Boolean, // 0x42 B
                68 => isList ? ValueType.DoubleArray : ValueType.Double,   // 0x44 D
                76 => isList ? ValueType.LongArray : ValueType.Long,       // 0x4c L


                65 or 67 or 80 or 83 or 84 => isList ? ValueType.ULongArray : ValueType.ULong,
                82 => isList ? ValueType.RawJsonArray : ValueType.RawJson,

                _ => ValueType.String
            };
        }
        private static void SetIndexValue(object target, PrototypeClassManager.EnhancedFieldInfo fieldInfo, int index, ValueBase value)
        {
            var propertyInfo = fieldInfo.PropertyInfo;

            if (propertyInfo.GetValue(target) is not Array array)
                return;

            if (index < 0 || index >= array.Length)
                return;

            Type elementType = fieldInfo.ElementType;
            object valueEntry = value.GetValue();
            object finalValue;

            if (typeof(Prototype).IsAssignableFrom(elementType) && (valueEntry is PrototypeId || valueEntry is ulong || value.ValueType == ValueType.PrototypeDataRef))
            {
                var dataRef = (PrototypeId)Convert.ChangeType(valueEntry, typeof(ulong));
                finalValue = GameDatabase.GetPrototype<Prototype>(dataRef);
                if (finalValue == null)
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype or could not be found.");
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
                throw new InvalidOperationException($"The resolved value of type {finalValue.GetType().Name} cannot be assigned to an array element of type {elementType.Name}.");

            array.SetValue(finalValue, index);
        }

        private static void InsertValue(object target, PrototypeClassManager.EnhancedFieldInfo fieldInfo, ValueBase value)
        {
            var propertyInfo = fieldInfo.PropertyInfo;

            if (!fieldInfo.IsArray)
                throw new InvalidOperationException($"Field {propertyInfo.Name} is not an array.");

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

        private object GetOrCreateObjectFromPath(object root, string path, string targetFieldName = null)
        {
            if (string.IsNullOrEmpty(path)) return root;

            object current = root;
            var pathParts = path.Split('.');

            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (current == null) return null;

                // Handle BlueprintId-keyed mixin selectors dynamically
                if (part.Contains("[BlueprintId="))
                {
                    int bracketStart = part.IndexOf('[');
                    string memberName = part.Substring(0, bracketStart);

                    var matchText = part.Substring(bracketStart + 1, part.Length - bracketStart - 2);
                    ulong blueprintId = 0;
                    byte copyNum = 0;

                    foreach (var segment in matchText.Split(','))
                    {
                        var kv = segment.Split('=');
                        if (kv.Length != 2) continue;
                        if (kv[0] == "BlueprintId") ulong.TryParse(kv[1], out blueprintId);
                        if (kv[0] == "Copy") byte.TryParse(kv[1], out copyNum);
                    }

                    // Changed to object to allow flattened C# classes to be returned
                    object foundMixinObj = null;

                    // Try to find the mixin in the explicit list (e.g., "Components")
                    object memberValue = string.IsNullOrEmpty(memberName) ? null : GetMemberValue(current, memberName, out _);
                    if (memberValue is PrototypeMixinList namedList)
                    {
                        for (int mIdx = 0; mIdx < namedList.Count; mIdx++)
                        {
                            var mixin = namedList[mIdx];
                            if (mixin?.Prototype == null) continue;

                            if ((ulong)mixin.BlueprintId == blueprintId && mixin.BlueprintCopyNum == copyNum)
                            {
                                foundMixinObj = mixin.Prototype;
                                break;
                            }

                            if (!string.IsNullOrEmpty(targetFieldName))
                            {
                                var fieldInfo = GameDatabase.PrototypeClassManager.GetFieldByName(mixin.Prototype.GetType(), targetFieldName);
                                if (fieldInfo.HasValue)
                                {
                                    foundMixinObj = mixin.Prototype;
                                    break;
                                }
                            }
                        }
                    }

                    //FALLBACK: Search ALL PrototypeMixinList fields dynamically
                    if (foundMixinObj == null)
                    {
                        var type = current.GetType();

                        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (prop.GetIndexParameters().Length > 0) continue;

                            if (typeof(PrototypeMixinList).IsAssignableFrom(prop.PropertyType))
                            {
                                if (prop.GetValue(current) is PrototypeMixinList mixinList)
                                {
                                    for (int mIdx = 0; mIdx < mixinList.Count; mIdx++)
                                    {
                                        var mixin = mixinList[mIdx];
                                        if (mixin?.Prototype == null) continue;

                                        if ((ulong)mixin.BlueprintId == blueprintId && mixin.BlueprintCopyNum == copyNum)
                                        {
                                            foundMixinObj = mixin.Prototype;
                                            break;
                                        }

                                        if (!string.IsNullOrEmpty(targetFieldName))
                                        {
                                            var fieldInfo = GameDatabase.PrototypeClassManager.GetFieldByName(mixin.Prototype.GetType(), targetFieldName);
                                            if (fieldInfo.HasValue)
                                            {
                                                foundMixinObj = mixin.Prototype;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            if (foundMixinObj != null) break;
                        }

                        if (foundMixinObj == null)
                        {
                            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (typeof(PrototypeMixinList).IsAssignableFrom(field.FieldType))
                                {
                                    if (field.GetValue(current) is PrototypeMixinList mixinList)
                                    {
                                        for (int mIdx = 0; mIdx < mixinList.Count; mIdx++)
                                        {
                                            var mixin = mixinList[mIdx];
                                            if (mixin?.Prototype == null) continue;

                                            if ((ulong)mixin.BlueprintId == blueprintId && mixin.BlueprintCopyNum == copyNum)
                                            {
                                                foundMixinObj = mixin.Prototype;
                                                break;
                                            }

                                            if (!string.IsNullOrEmpty(targetFieldName))
                                            {
                                                var fieldInfo = GameDatabase.PrototypeClassManager.GetFieldByName(mixin.Prototype.GetType(), targetFieldName);
                                                if (fieldInfo.HasValue)
                                                {
                                                    foundMixinObj = mixin.Prototype;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (foundMixinObj != null) break;
                            }
                        }
                    }

                    // C# FLATTENED MIXIN FALLBACK
                    // In MHServerEmu, mixins are often flattened into strongly typed C# properties instead of being kept in a list!
                    // Example: OC has "Components[...].Speed", but the Server has `public LocomotionComponent Locomotion { get; set; }`
                    if (foundMixinObj == null && !string.IsNullOrEmpty(targetFieldName))
                    {
                        var type = current.GetType();

                        // Check all custom class properties attached to AvatarPrototype
                        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (prop.GetIndexParameters().Length > 0) continue;
                            if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType) continue;

                            var fieldInfo = GameDatabase.PrototypeClassManager.GetFieldByName(prop.PropertyType, targetFieldName);
                            if (fieldInfo.HasValue)
                            {
                                // We found the C# class that holds the field!
                                foundMixinObj = prop.GetValue(current);
                                break;
                            }
                        }

                        // Check all custom class fields
                        if (foundMixinObj == null)
                        {
                            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string) || field.FieldType.IsValueType) continue;

                                var fieldInfo = GameDatabase.PrototypeClassManager.GetFieldByName(field.FieldType, targetFieldName);
                                if (fieldInfo.HasValue)
                                {
                                    foundMixinObj = field.GetValue(current);
                                    break;
                                }
                            }
                        }
                    }

                    // 4. Return to root if completely lost
                    if (foundMixinObj == null)
                    {
                        return current;
                    }

                    current = foundMixinObj;
                    continue;
                }

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
                        if (memberType == null) return null;
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
                    return null;

                if (nextObj is PrototypeId pid && pid != PrototypeId.Invalid)
                {
                    var resolvedProto = GameDatabase.GetPrototype<Prototype>(pid);
                    if (resolvedProto != null) nextObj = resolvedProto;
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

        private object GetMemberValue(object obj, string memberName, out Type memberType)
        {
            memberType = null;
            if (obj == null) return null;

            var type = obj.GetType();

            var propInfo = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (propInfo != null)
            {
                memberType = propInfo.PropertyType;
                return propInfo.GetValue(obj);
            }

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
                fieldInfo.SetValue(obj, value);
        }

        private static object GetElementValue(object valueEntry, Type elementType)
        {
            if (typeof(EvalPrototype).IsAssignableFrom(elementType) && valueEntry is EvalPrototype)
                return valueEntry;

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is Prototype)
                return valueEntry;

            if (typeof(Prototype).IsAssignableFrom(elementType) && valueEntry is PrototypeId dataRef)
            {
                var result = GameDatabase.GetPrototype<Prototype>(dataRef);
                if (result == null)
                    throw new InvalidOperationException($"DataRef {dataRef} is not a valid Prototype.");
                return result;
            }

            return ConvertValue(valueEntry, elementType);
        }

        public static object ConvertValue(object rawValue, Type targetType)
        {
            if (rawValue == null || (rawValue is JsonElement jsonValCheck && jsonValCheck.ValueKind == JsonValueKind.Null))
                return null;

            if (targetType.IsInstanceOfType(rawValue))
                return rawValue;

            if (typeof(EvalPrototype).IsAssignableFrom(targetType) && rawValue is EvalPrototype evalProto)
                return evalProto;

            if (typeof(Prototype).IsAssignableFrom(targetType) && rawValue is Prototype proto)
                return proto;

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
                                parameters[i] = (PropertyParam)paramElement.GetUInt64();
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
                if (jsonVal.ValueKind == JsonValueKind.Array && targetType.IsArray)
                {
                    Type elementType = targetType.GetElementType();
                    var jsonArray = jsonVal.EnumerateArray().ToArray();

                    Array newArray = Array.CreateInstance(elementType, jsonArray.Length);
                    for (int i = 0; i < jsonArray.Length; i++)
                        newArray.SetValue(ConvertValue(jsonArray[i], elementType), i);

                    return newArray;
                }

                if (typeof(EvalPrototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                        return PatchEntryConverter.ParseJsonEval(jsonVal);
                }

                if (typeof(Prototype).IsAssignableFrom(targetType) && jsonVal.ValueKind == JsonValueKind.Object)
                {
                    if (jsonVal.TryGetProperty("ParentDataRef", out _))
                        return PatchEntryConverter.ParseJsonPrototype(jsonVal);
                }

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
                                    parameters[i] = (PropertyParam)paramElement.GetUInt64();
                            }

                            return new PropertyId(propertyEnum, parameters[0], parameters[1], parameters[2], parameters[3]);
                        }
                    }
                    catch (Exception ex)
                    {
                        return null;
                    }
                }

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
                        return PatchEntryConverter.ParseAndValidateEnum(jsonVal, targetType);
                }
                catch (Exception ex)
                {
                }
            }

            if (rawValue is JsonElement[] jsonElementArray && targetType.IsArray)
            {
                Type elementType = targetType.GetElementType();
                Array newArray = Array.CreateInstance(elementType, jsonElementArray.Length);
                for (int i = 0; i < jsonElementArray.Length; i++)
                    newArray.SetValue(ConvertValue(jsonElementArray[i], elementType), i);
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
                        return AssetId.Invalid;

                    var assetType = GameDatabase.DataDirectory.AssetDirectory.GetAssetType(assetTypeName);
                    if (assetType == null)
                        return AssetId.Invalid;

                    return assetType.FindAssetByName(assetName, DataFileSearchFlags.CaseInsensitive);
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

            if (targetType.IsEnum)
            {
                if (rawValue is long l) return Enum.ToObject(targetType, l);
                if (rawValue is ulong ul) return Enum.ToObject(targetType, ul);
                if (rawValue is int i) return Enum.ToObject(targetType, i);
            }

            if (rawValue is ulong[] ulongArray)
            {
                if (targetType.IsArray && targetType.GetElementType()?.IsEnum == true)
                {
                    Type elementType = targetType.GetElementType();
                    Array newArray = Array.CreateInstance(elementType, ulongArray.Length);
                    for (int i = 0; i < ulongArray.Length; i++)
                    {
                        newArray.SetValue(Enum.ToObject(elementType, ulongArray[i]), i);
                    }
                    return newArray;
                }

                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>) && targetType.GetGenericArguments()[0].IsEnum)
                {
                    Type elementType = targetType.GetGenericArguments()[0];
                    IList newList = (IList)Activator.CreateInstance(targetType);
                    foreach (ulong val in ulongArray)
                    {
                        newList.Add(Enum.ToObject(elementType, val));
                    }
                    return newList;
                }
            }

            if (targetType == typeof(float) && rawValue is double d)
            {
                return (float)d;
            }


            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(rawValue.GetType()))
                return converter.ConvertFrom(rawValue);

            return Convert.ChangeType(rawValue, targetType);
        }

    }
}