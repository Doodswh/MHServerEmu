using System.Reflection;
using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.GameData
{
    /// <summary>
    /// Wrapper for <see cref="System.Reflection.PropertyInfo"/> to manage Prototype class fields.
    /// </summary>
    public class PrototypeFieldInfo
    {
        private static readonly Dictionary<Type, SymbolicLookup<int>> EnumLookups = new();

        private readonly Array _emptyCollection;

        private Delegate _getDelegate;
        private Delegate _setDelegate;
        private Action<Prototype, Prototype> _copyDelegate;
        private Action<Prototype, Prototype> _copyArrayDelegate;

        public System.Reflection.PropertyInfo PropertyInfo { get; }
        public PrototypeFieldType Type { get; }

        public Type ListElementType { get; }
        public SymbolicLookup<int> SymbolicEnum { get; }

        public string Name { get => PropertyInfo.Name; }
        public Type ClassType { get => PropertyInfo.PropertyType; }     // The client uses numeric class ids for this

        public PrototypeFieldInfo(System.Reflection.PropertyInfo propertyInfo, PrototypeFieldType fieldType)
        {
            PropertyInfo = propertyInfo;
            Type = fieldType;

            // Cache additional type-specific metadata
            switch (fieldType)
            {
                case PrototypeFieldType.Enum:
                    SymbolicEnum = GetSymbolicLookup(ClassType);
                    break;

                case PrototypeFieldType.ListEnum:
                case PrototypeFieldType.ListPrototypePtr:
                case PrototypeFieldType.VectorPrototypeRefPtr:
                    ListElementType = PropertyInfo.PropertyType.GetElementType();
                    _emptyCollection = Array.CreateInstance(ListElementType, 0);

                    if (fieldType == PrototypeFieldType.ListEnum)
                        SymbolicEnum = GetSymbolicLookup(ListElementType);

                    break;

                case PrototypeFieldType.ListMixin:
                    PrototypeFieldAttribute prototypeFieldAttribute = PropertyInfo.GetCustomAttribute<PrototypeFieldAttribute>();
                    if (prototypeFieldAttribute != null)
                        ListElementType = prototypeFieldAttribute.Param as Type;
                    break;
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public Array AllocateCollection(int length)
        {
            if (ListElementType == null)
                return null;

            if (length == 0)
                return _emptyCollection;

            return Array.CreateInstance(ListElementType, length);
        }

        public void GetValue<T>(Prototype prototype, out T value)
        {
            _getDelegate ??= PropertyInfo.CreateGetDelegate<Prototype, T>();
            Func<Prototype, T> get = (Func<Prototype, T>)_getDelegate;
            value = get(prototype);
        }

        public void SetValue<T>(Prototype prototype, T value)
        {
            _setDelegate ??= PropertyInfo.CreateSetDelegate<Prototype, T>();
            Action<Prototype, T> set = (Action<Prototype, T>)_setDelegate;
            set(prototype, value);
        }

        public void CopyValue(Prototype source, Prototype destination)
        {
            _copyDelegate ??= PropertyInfo.CreateCopyDelegate<Prototype>();
            _copyDelegate(source, destination);
        }

        public void CopyArray(Prototype source, Prototype destination)
        {
            _copyArrayDelegate ??= PropertyInfo.CreateCopyArrayDelegate<Prototype>();
            _copyArrayDelegate(source, destination);
        }

        private SymbolicLookup<int> GetSymbolicLookup(Type enumType)
        {
            // Cache lookups to reuse for different instances of PrototypeFieldInfo.
            if (EnumLookups.TryGetValue(enumType, out SymbolicLookup<int> enumLookup) == false)
            {
                AssetEnumAttribute assetEnumAttribute = PropertyInfo.PropertyType.GetCustomAttribute<AssetEnumAttribute>();
                int defaultValue = assetEnumAttribute != null ? assetEnumAttribute.DefaultValue : 0;
                enumLookup = new(enumType, defaultValue);
                EnumLookups.Add(enumType, enumLookup);
            }

            return enumLookup;
        }
    }
}
