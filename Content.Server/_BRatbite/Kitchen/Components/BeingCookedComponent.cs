namespace Content.Server._BRatbite.Kitchen.Components;

[RegisterComponent]
public sealed partial class BeingCookedComponent : Component
{
    [DataField]
    public EntityUid? Cookware;
}
