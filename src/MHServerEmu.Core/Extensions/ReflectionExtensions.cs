using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace MHServerEmu.Core.Extensions
{
    public static class ReflectionExtensions
    {
        private static readonly MethodInfo ArrayCloneMethod = typeof(Array).GetMethod("Clone");

        private static readonly Dictionary<PropertyInfo, FieldInfo> PropertyBackingFields = new();
        private static readonly Dictionary<PropertyInfo, Delegate> SetPropertyDelegates = new();
        private static readonly Dictionary<PropertyInfo, Delegate> CopyPropertyDelegates = new();
        private static readonly Dictionary<PropertyInfo, Delegate> CopyArrayPropertyDelegates = new();

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
        /// Sets the value of the auto property represented by this <see cref="PropertyInfo"/> avoiding boxing.
        /// </summary>
        public static void SetValueFast<TInstance, TValue>(this PropertyInfo propertyInfo, TInstance instance, TValue value)
        {
            if (SetPropertyDelegates.TryGetValue(propertyInfo, out Delegate setDelegate) == false)
            {
                FieldInfo fieldInfo = propertyInfo.GetBackingField();

                DynamicMethod dm = new("SetValue", null, [typeof(TInstance), typeof(TValue)]);
                ILGenerator il = dm.GetILGenerator();

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, fieldInfo);
                il.Emit(OpCodes.Ret);

                setDelegate = dm.CreateDelegate<Action<TInstance, TValue>>();
                SetPropertyDelegates.Add(propertyInfo, setDelegate);
            }

            Action<TInstance, TValue> set = (Action<TInstance, TValue>)setDelegate;
            set(instance, value);
        }

        /// <summary>
        /// Copies the value of the auto property represented by this <see cref="PropertyInfo"/> from one instance to another.
        /// </summary>
        public static void CopyValue<T>(this PropertyInfo propertyInfo, T source, T destination)
        {
            if (CopyPropertyDelegates.TryGetValue(propertyInfo, out Delegate copyDelegate) == false)
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
                CopyPropertyDelegates.Add(propertyInfo, copyDelegate);
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
            if (CopyArrayPropertyDelegates.TryGetValue(propertyInfo, out Delegate copyArrayDelegate) == false)
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
                CopyArrayPropertyDelegates.Add(propertyInfo, copyArrayDelegate);
            }

            Action<T, T> copyArray = (Action<T, T>)copyArrayDelegate;
            copyArray(source, destination);
        }
    }
}
