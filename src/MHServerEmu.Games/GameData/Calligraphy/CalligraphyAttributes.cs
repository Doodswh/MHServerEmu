namespace MHServerEmu.Games.GameData.Calligraphy
{
    /// <summary>
    /// Indicates that an enum has a corresponding Calligraphy asset type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum)]
    public class AssetEnumAttribute(int defaultValue = 0, string assetBinding = null) : Attribute
    {
        public int DefaultValue { get; } = defaultValue;
        public string AssetBinding { get; } = assetBinding;
    }

    /// <summary>
    /// Explicitly assigns a <see cref="PrototypeFieldType"/> to a C# property representing a prototype field.
    /// </summary>
    /// <remarks>
    /// When this attribute is not assigned, <see cref="PrototypeFieldType"/> is determined dynamically using the underlying C# property type.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public class PrototypeFieldAttribute(PrototypeFieldType type, object param = null) : Attribute
    {
        public PrototypeFieldType Type { get; } = type;
        public object Param { get; } = param;
    }

    /// <summary>
    /// Indicates that a property needs to be ignored when copying prototype fields.
    /// </summary>
    /// <remarks>
    /// This is the same as specifying <see cref="PrototypeFieldType.Invalid"/> via <see cref="PrototypeFieldAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DoNotCopyAttribute : PrototypeFieldAttribute
    {
        // Exclude from copying by explicitly specifying the invalid field type
        public DoNotCopyAttribute() : base(PrototypeFieldType.Invalid)
        {
        }
    }
}
