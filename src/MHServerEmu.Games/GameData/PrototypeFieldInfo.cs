using System.Reflection;
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
        public System.Reflection.PropertyInfo PropertyInfo { get; }
        public PrototypeFieldType Type { get; }

        public Type ListElementType { get; }
        public int DefaultEnumValue { get; }

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
                    AssetEnumAttribute assetEnumAttribute = PropertyInfo.PropertyType.GetCustomAttribute<AssetEnumAttribute>();
                    if (assetEnumAttribute != null)
                        DefaultEnumValue = assetEnumAttribute.DefaultValue;
                    break;

                case PrototypeFieldType.ListEnum:
                case PrototypeFieldType.ListPrototypePtr:
                case PrototypeFieldType.VectorPrototypeRefPtr:
                    ListElementType = PropertyInfo.PropertyType.GetElementType();
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

        public void GetValue<T>(Prototype prototype, out T value)
        {
            PropertyInfo.GetValue(prototype, out value);
        }

        public void SetValue<T>(Prototype prototype, T value)
        {
            PropertyInfo.SetValueFast(prototype, value);
        }

        public void CopyValue(Prototype source, Prototype destination)
        {
            PropertyInfo.CopyValue(source, destination);
        }

        public void CopyArray(Prototype source, Prototype destination)
        {
            PropertyInfo.CopyArray(source, destination);
        }
    }
}
