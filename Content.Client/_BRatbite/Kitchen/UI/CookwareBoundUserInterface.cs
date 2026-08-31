using Content.Shared._BRatbite.Kitchen;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._BRatbite.Kitchen.UI;

public sealed class CookwareBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private CookwareMenu? _menu;

    [ViewVariables]
    private readonly Dictionary<int, EntityUid> _solids = new();


    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<CookwareMenu>();
        _menu.EjectButton.OnPressed += _ => SendPredictedMessage(new CookwareEjectMessage());
        _menu.IngredientsList.OnItemSelected += args =>
        {
            SendPredictedMessage(
                new CookwareEjectSolidIndexedMessage(EntMan.GetNetEntity(_solids[args.ItemIndex])));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not CookwareUpdateUserInterfaceState cState || _menu == null)
        {
            return;
        }

        _menu.EjectButton.Disabled = cState.ContainedSolids.Length == 0;

        RefreshContentsDisplay(EntMan.GetEntityArray(cState.ContainedSolids));
    }

    private void RefreshContentsDisplay(EntityUid[] containedSolids)
    {
        if (_menu == null)
            return;

        _solids.Clear();
        _menu.IngredientsList.Clear();
        foreach (var entity in containedSolids)
        {
            if (EntMan.Deleted(entity))
                return;

            Texture? texture;
            if (EntMan.TryGetComponent<IconComponent>(entity, out var iconComponent))
            {
                texture = EntMan.System<SpriteSystem>().GetIcon(iconComponent);
            }
            else if (EntMan.TryGetComponent<SpriteComponent>(entity, out var spriteComponent))
            {
                texture = spriteComponent.Icon?.Default;
            }
            else
            {
                continue;
            }

            var solidItem =
                _menu.IngredientsList.AddItem(EntMan.GetComponent<MetaDataComponent>(entity).EntityName, texture);
            var solidIndex = _menu.IngredientsList.IndexOf(solidItem);
            _solids.Add(solidIndex, entity);
        }
    }
}
