using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class Prototype
    {
        // Child prototypes need to have separate mixin lists from their parents so that when we modify a child we don't change its parent.
        // To ensure this each prototype keeps track of mixins that belong to it in this field. This is accurate to how the client does this.
        // The client uses a sorted vector here, but we use a HashSet to ensure that the same field data doesn't get added twice for some reason.
        private HashSet<object> _ownedDynamicFields;

        public PrototypeId DataRef { get; set; }
        public PrototypeId ParentDataRef { get; set; }
        public PrototypeDataRefRecord DataRefRecord { get; set; }

        /// <summary>
        /// Returns <see cref="true"/> if this <see cref="Prototype"/> needs to have its CRC calculated.
        /// </summary>
        public virtual bool ShouldCacheCRC { get => false; }

        public override string ToString()
        {
            return DataRef != PrototypeId.Invalid ? GameDatabase.GetPrototypeName(DataRef) : base.ToString();
        }

        /// <summary>
        /// Returns <see langword="true"/> if this prototype is approved for use. Approval criteria differ depending on the prototype type.
        /// </summary>
        public virtual bool ApprovedForUse()
        {
            return true;
        }

        /// <summary>
        /// Post-processes data contained in this <see cref="Prototype"/>.
        /// </summary>
        /// <remarks>
        /// Concrete prototype types override this to post-process their data after loading.
        /// </remarks>
        public virtual void PostProcess()
        {
            GameDatabase.PrototypeClassManager.PostProcessContainedPrototypes(this);
        }

        /// <summary>
        /// PreCheck data contained in this <see cref="Prototype"/>.
        /// </summary>
        public void PreCheck()
        {
            GameDatabase.PrototypeClassManager.PreCheck(this);
        }

        // These dynamic field management methods are part of the PrototypeClassManager in the client, but it doesn't really make sense so we moved them here.
        // They work only with reference types, but we use them only for list mixins, so it's fine.

        /// <summary>
        /// Assigns this prototype as the owner of field data of type <typeparamref name="T"/>. Field data must be a reference type.
        /// </summary>
        public void SetDynamicFieldOwner<T>(T fieldData) where T: class
        {
            _ownedDynamicFields ??= new();
            _ownedDynamicFields.Add(fieldData);
        }

        /// <summary>
        /// Removes this prototype as the owner of field data of type <typeparamref name="T"/>. Field data must be a reference type.
        /// </summary>
        public void RemoveDynamicFieldOwner<T>(T fieldData) where T: class
        {
            if (!Verify.IsNotNull(_ownedDynamicFields)) return;
            Verify.IsTrue(_ownedDynamicFields.Remove(fieldData));
        }

        /// <summary>
        /// Checks if this prototype is the owner of field data of type <typeparamref name="T"/>. Field data must be a reference type.
        /// </summary>
        public bool IsDynamicFieldOwnedBy<T>(T fieldData) where T: class
        {
            if (_ownedDynamicFields == null)
                return false;

            return _ownedDynamicFields.Contains(fieldData);
        }

        public int GetEnumValueFromBlueprint(BlueprintId blueprintId)
        {
            // Fall back to parent prototype for RHStructs
            PrototypeId protoRef = DataRef == PrototypeId.Invalid ? ParentDataRef : DataRef;

            if (protoRef == PrototypeId.Invalid)
                return 0;

            DataOrigin dataOrigin = DataDirectory.Instance.GetDataOrigin(protoRef);
            if (dataOrigin != DataOrigin.Calligraphy && dataOrigin != DataOrigin.Dynamic)
                return 0;

            Blueprint blueprint = DataDirectory.Instance.GetBlueprint(blueprintId);
            if (!Verify.IsNotNull(blueprint)) return 0;

            return blueprint.GetPrototypeEnumValue(protoRef);
        }
    }
}
