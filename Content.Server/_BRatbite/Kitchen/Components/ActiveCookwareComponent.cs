using Content.Shared.Kitchen;

namespace Content.Server._BRatbite.Kitchen.Components;


[RegisterComponent]
public sealed partial class ActiveCookwareComponent : Component
{
    public float CookTimeRemaining;

    public FoodRecipePrototype? Recipe;

    public EntityUid Heater;
}
