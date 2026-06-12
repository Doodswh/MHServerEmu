using System.Reflection;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Games.GameData.Calligraphy.Attributes;
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

        public string Name { get => PropertyInfo.Name; }
        public Type ClassType { get => PropertyInfo.PropertyType; }
        public Type ListMixinType { get => PropertyInfo.GetCustomAttribute<ListMixinAttribute>()?.FieldType; }
        public int EnumDefaultValue { get => PropertyInfo.PropertyType.GetCustomAttribute<AssetEnumAttribute>().DefaultValue; }
        public Type ElementType { get => PropertyInfo.PropertyType.GetElementType(); }

        public PrototypeFieldInfo(System.Reflection.PropertyInfo propertyInfo, PrototypeFieldType fieldType)
        {
            PropertyInfo = propertyInfo;
            Type = fieldType;
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
