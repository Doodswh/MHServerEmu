using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Helpers;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace MHServerEmu.Core.Extensions
{
    public static class ReflectionExtensions
    {
        private static readonly MethodInfo ArrayCloneMethod = typeof(Array).GetMethod("Clone");

        private static readonly Dictionary<PropertyInfo, FieldInfo> PropertyBackingFields = new();
        private static readonly Dictionary<PropertyInfo, InlineArray4<Delegate>> PropertyDelegates = new();

        private static readonly Dictionary<Type, Dictionary<uint, int>> EnumLookups = new();

        // Notes:
        // - Reflection.Emit is faster than expression trees.
        // - Get/set using FieldInfo is faster than PropertyInfo.
        // - Use generic delegates to avoid value type boxing.
        // - Reflection is expensive, so cache everything.

        /// <summary>
        /// Returns the <see cref="FieldInfo"/> for the backing field of the auto property represented by this <see cref="PropertyInfo"/>.
        /// </summary>
        public static FieldInfo GetBackingField(this PropertyInfo propertyInfo)
        {
            if (PropertyBackingFields.TryGetValue(propertyInfo, out FieldInfo fieldInfo) == false)
            {
                string backingFieldName = $"<{propertyInfo.Name}>k__BackingField";
                fieldInfo = propertyInfo.DeclaringType.GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                Debug.Assert(fieldInfo != null);
                PropertyBackingFields.Add(propertyInfo, fieldInfo);
            }

            return fieldInfo;
        }

        /// <summary>
        /// Retrieves the value of the auto property represented by this <see cref="PropertyInfo"/> avoiding boxing.
        /// </summary>
        public static void GetValue<TInstance, TValue>(this PropertyInfo propertyInfo, TInstance instance, out TValue value)
        {
            ref Delegate getDelegate = ref GetDelegateRef(propertyInfo, PropertyDelegate.Get);
            if (getDelegate == null)
            {
                FieldInfo fieldInfo = propertyInfo.GetBackingField();

                DynamicMethod dm = new("GetValue", typeof(TValue), [typeof(TInstance)]);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldInfo);
                il.Emit(OpCodes.Ret);

                getDelegate = dm.CreateDelegate<Func<TInstance, TValue>>();
            }

            Func<TInstance, TValue> get = (Func<TInstance, TValue>)getDelegate;
            value = get(instance);
        }

        /// <summary>
        /// Sets the value of the auto property represented by this <see cref="PropertyInfo"/> avoiding boxing.
        /// </summary>
        public static void SetValueFast<TInstance, TValue>(this PropertyInfo propertyInfo, TInstance instance, TValue value)
        {
            ref Delegate setDelegate = ref GetDelegateRef(propertyInfo, PropertyDelegate.Set);
            if (setDelegate == null)
            {
                FieldInfo fieldInfo = propertyInfo.GetBackingField();

                DynamicMethod dm = new("SetValue", null, [typeof(TInstance), typeof(TValue)]);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, fieldInfo);
                il.Emit(OpCodes.Ret);

                setDelegate = dm.CreateDelegate<Action<TInstance, TValue>>();
            }

            Action<TInstance, TValue> set = (Action<TInstance, TValue>)setDelegate;
            set(instance, value);
        }

        /// <summary>
        /// Copies the value of the auto property represented by this <see cref="PropertyInfo"/> from one instance to another.
        /// </summary>
        public static void CopyValue<T>(this PropertyInfo propertyInfo, T source, T destination)
        {
            ref Delegate copyDelegate = ref GetDelegateRef(propertyInfo, PropertyDelegate.Copy);
            if (copyDelegate == null)
            {
                FieldInfo fieldInfo = propertyInfo.GetBackingField();

                DynamicMethod dm = new("CopyValue", null, [typeof(T), typeof(T)]);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldInfo);
                il.Emit(OpCodes.Stfld, fieldInfo);
                il.Emit(OpCodes.Ret);

                copyDelegate = dm.CreateDelegate<Action<T, T>>();
            }

            Action<T, T> copy = (Action<T, T>)copyDelegate;
            copy(source, destination);
        }

        /// <summary>
        /// Creates a shallow copy of the array value of the auto property represented by this <see cref="PropertyInfo"/>
        /// and assigns it to the destination instance.
        /// </summary>
        public static void CopyArray<T>(this PropertyInfo propertyInfo, T source, T destination)
        {
            ref Delegate copyArrayDelegate = ref GetDelegateRef(propertyInfo, PropertyDelegate.CopyArray);
            if (copyArrayDelegate == null)
            {
                FieldInfo fieldInfo = propertyInfo.GetBackingField();
                Type fieldType = fieldInfo.FieldType;
                Debug.Assert(fieldType.IsAssignableTo(typeof(Array)));

                DynamicMethod dm = new("CopyArrayValue", null, [typeof(T), typeof(T)]);
                ILGenerator il = dm.GetILGenerator();

                il.DeclareLocal(fieldInfo.FieldType);
                Label retLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldInfo);
                il.Emit(OpCodes.Stloc_0);

                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Brfalse_S, retLabel);

                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Callvirt, ArrayCloneMethod);
                il.Emit(OpCodes.Castclass, fieldType);
                il.Emit(OpCodes.Stloc_0);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Stfld, fieldInfo);

                il.MarkLabel(retLabel);
                il.Emit(OpCodes.Ret);

                copyArrayDelegate = dm.CreateDelegate<Action<T, T>>();
            }

            Action<T, T> copyArray = (Action<T, T>)copyArrayDelegate;
            copyArray(source, destination);
        }

        /// <summary>
        /// Retrieves the <see cref="int"/> representation of an <see cref="Enum"/> value with the provided name.
        /// </summary>
        /// <remarks>
        /// If an enum member name starts with an underscore prefix, the underscore character will be ignored.
        /// </remarks>
        public static bool TryGetEnumValue(this Type type, ReadOnlySpan<char> name, out int value)
        {
            Debug.Assert(type.IsEnum);
            Debug.Assert(type.GetEnumUnderlyingType() == typeof(int));

            if (EnumLookups.TryGetValue(type, out Dictionary<uint, int> enumLookup) == false)
            {
                enumLookup = new();

                // Multiple names can have the same value, so we need to iterate names and parse values and not vice versa.
                foreach (string enumName in Enum.GetNames(type))
                {
                    int enumValue = (int)Enum.Parse(type, enumName);

                    // Remove the underscore prefix we add for C# compatibility
                    ReadOnlySpan<char> chars = enumName;
                    if (chars[0] == '_')
                        chars = chars[1..];

                    uint hash = HashHelper.Djb2(chars);
                    enumLookup.Add(hash, enumValue);
                }

                EnumLookups.Add(type, enumLookup);
            }

            return enumLookup.TryGetValue(HashHelper.Djb2(name), out value);
        }

        private static ref Delegate GetDelegateRef(PropertyInfo propertyInfo, PropertyDelegate delegateEnum)
        {
            ref InlineArray4<Delegate> delegates = ref PropertyDelegates.GetValueRefOrAddDefault(propertyInfo);
            return ref delegates[(int)delegateEnum];
        }

        private enum PropertyDelegate
        {
            Get,
            Set,
            Copy,
            CopyArray,
            NumDelegates,
        }
    }
}
