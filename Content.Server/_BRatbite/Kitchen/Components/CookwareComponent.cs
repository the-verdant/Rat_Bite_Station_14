using Content.Shared.Item;
using Content.Shared.Kitchen;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._BRatbite.Kitchen.Components;

[RegisterComponent]
public sealed partial class CookwareComponent : Component
{

    [DataField("failureResult", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string BadRecipeEntityId = "FoodBadRecipe";

    public Container Storage = default!;

    [DataField]
    public string ContainerId = "cookware_entity_container";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Capacity = 10;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ItemSizePrototype> MaxItemSize = "Normal";
}
