using System.Linq;
using Content.Goobstation.Maths.FixedPoint;
using Content.Server._BRatbite.Kitchen.Components;
using Content.Server.Chemistry.Components;
using Content.Server.Construction;
using Content.Server.Hands.Systems;
using Content.Server.Power.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._BRatbite.Kitchen;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._BRatbite.Kitchen.EntitySystems;

public sealed class CookwareSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> PlasticTag = "Plastic";
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly RecipeManager _recipeManager = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CookwareComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CookwareComponent, SolutionContainerChangedEvent>((ent, ref _) =>
            UpdateUserInterfaceState(ent));
        SubscribeLocalEvent<CookwareComponent, EntInsertedIntoContainerMessage>(OnContentUpdate);
        SubscribeLocalEvent<CookwareComponent, EntRemovedFromContainerMessage>(OnContentUpdate);
        SubscribeLocalEvent<CookwareComponent, InteractUsingEvent>(OnInteractUsing,
            after: new[] { typeof(AnchorableSystem) });
        SubscribeLocalEvent<CookwareComponent, CookwareEjectMessage>(OnEjectMessage);
        SubscribeLocalEvent<CookwareComponent, CookwareEjectSolidIndexedMessage>(OnEjectIndex);
        SubscribeLocalEvent<ActiveCookwareComponent, EntInsertedIntoContainerMessage>(
            OnActiveCookwareInsert);
        SubscribeLocalEvent<ActiveCookwareComponent, EntRemovedFromContainerMessage>(
            OnActiveCookwareRemove);
        SubscribeLocalEvent<BeingCookedComponent, OnConstructionTemperatureEvent>(OnConstructionTemp);
        SubscribeLocalEvent<BeingCookedComponent, SolutionRelayEvent<ReactionAttemptEvent>>(
            OnReactionAttempt);
        SubscribeLocalEvent<CookwareComponent, GetVerbsEvent<AlternativeVerb>>(AddVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var activeSolutionHeaters = EntityQueryEnumerator<ActiveSolutionHeaterComponent, ItemPlacerComponent>();
        while (activeSolutionHeaters.MoveNext(out var uid, out var activeHeaterComponent, out var placerComponent))
        {
            foreach (var placedEnt in placerComponent.PlacedEntities)
            {
                if (!TryComp<CookwareComponent>(placedEnt, out var cookwareComponent))
                    continue;

                StartCooking((placedEnt, cookwareComponent), uid);
            }
        }
        var activeGrilles = EntityQueryEnumerator<EntityHeaterComponent, ItemPlacerComponent, ApcPowerReceiverComponent>();
        while (activeGrilles.MoveNext(out var uid, out var entityHeaterComponent,
                   out var placerComponent,
                   out var apcPowerReceiverComponent))
        {
            if (!apcPowerReceiverComponent.Powered || entityHeaterComponent.Setting == EntityHeaterSetting.Off)
                continue;

            foreach (var placedEnt in placerComponent.PlacedEntities)
            {
                if (!TryComp<CookwareComponent>(placedEnt, out var cookwareComponent))
                    continue;

                StartCooking((placedEnt, cookwareComponent), uid);
            }
        }

        var activeCookware = EntityQueryEnumerator<ActiveCookwareComponent, CookwareComponent>();
        while (activeCookware.MoveNext(out var uid, out var activeCookwareComponent, out var cookwareComponent))
        {
            if (!TryComp<ItemPlacerComponent>(activeCookwareComponent.Heater, out var placerComponent))
            {
                StopCooking((uid, cookwareComponent, activeCookwareComponent));
                continue;
            }

            var placedEnts = placerComponent.PlacedEntities;
            if (!placedEnts.Contains(uid))
            {
                StopCooking((uid, cookwareComponent, activeCookwareComponent));
                continue;
            }


            if (HasComp<SolutionHeaterComponent>(activeCookwareComponent.Heater) && !HasComp<ActiveSolutionHeaterComponent>(activeCookwareComponent.Heater))
            {
                    StopCooking((uid, cookwareComponent, activeCookwareComponent));
                    continue;
            }

            if (TryComp<EntityHeaterComponent>(activeCookwareComponent.Heater, out var entityHeaterComponent))
            {
                if (!TryComp<ApcPowerReceiverComponent>(activeCookwareComponent.Heater,
                        out var apcPowerReceiverComponent) || !apcPowerReceiverComponent.Powered || entityHeaterComponent.Setting == EntityHeaterSetting.Off)
                {
                        StopCooking((uid, cookwareComponent, activeCookwareComponent));
                        continue;
                }
            }

            activeCookwareComponent.CookTimeRemaining -= frameTime;

            if (activeCookwareComponent.CookTimeRemaining > 0)
            {
                AddTemperature((uid, cookwareComponent), frameTime);
                continue;
            }

            AddTemperature((uid, cookwareComponent), frameTime);

            if (activeCookwareComponent.Recipe != null)
            {
                var coords = Transform(uid).Coordinates;
                SubtractContents((uid, cookwareComponent), activeCookwareComponent.Recipe);
                Spawn(activeCookwareComponent.Recipe.Result, coords);
            }

            UpdateUserInterfaceState((uid, cookwareComponent));
            StopCooking((uid, cookwareComponent, activeCookwareComponent));
        }
    }

    private void StopCooking(Entity<CookwareComponent, ActiveCookwareComponent> ent)
    {
        RemComp<ActiveCookwareComponent>(ent);
        foreach (var item in ent.Comp1.Storage.ContainedEntities)
        {
            if (!HasComp<BeingCookedComponent>(item))
                continue;
            RemComp<BeingCookedComponent>(item);
        }
    }

    private void AddTemperature(Entity<CookwareComponent> ent, float time)
    {
        foreach (var item in ent.Comp.Storage.ContainedEntities)
        {
            if (TryComp<TemperatureComponent>(item, out var temperatureComponent))
                _temperature.ChangeHeat(item, time, false, temperatureComponent);
        }

        if (!_solutionContainer.TryGetDrainableSolution(ent.Owner, out var solution, out var _))
            return;
        if (solution is { } soln) // avoids type checking complaints about null
            _solutionContainer.AddThermalEnergy(soln, time);
    }

    private void StartCooking(Entity<CookwareComponent> ent, EntityUid heaterUid)
    {
        if (HasComp<ActiveCookwareComponent>(ent))
            return;
        var (solids, reagents) = CollectIngredients(ent);
        var recipe = _recipeManager.Recipes.FirstOrDefault(r => CanSatisfyRecipe(r, solids, reagents));

        var activeCookwareComponent = AddComp<ActiveCookwareComponent>(ent);
        activeCookwareComponent.Heater = heaterUid;
        activeCookwareComponent.Recipe = recipe;
        activeCookwareComponent.CookTimeRemaining = recipe?.CookTime ?? 10;
        UpdateUserInterfaceState(ent);
    }

    private void SubtractContents(Entity<CookwareComponent> ent, FoodRecipePrototype recipe)
    {
        if (!_solutionContainer.TryGetDrainableSolution(ent.Owner, out var _, out var solution))
            return;

        foreach (var (reagent, quantity) in recipe.IngredientsReagents)
        {
            solution.RemoveReagent(reagent, quantity);
        }

        foreach (var recipeSolid in recipe.IngredientsSolids)
        {
            for (var i = 0; i < recipeSolid.Value; i++)
            {
                foreach (var item in ent.Comp.Storage.ContainedEntities)
                {
                    string? itemId = null;

                    // If an entity has a stack component, use the stacktype instead of prototype id
                    if (TryComp<StackComponent>(item, out var stackComp))
                    {
                        itemId = _prototype.Index(stackComp.StackTypeId).Spawn;
                    }
                    else
                    {
                        var metaData = MetaData(item);
                        if (metaData.EntityPrototype == null)
                        {
                            continue;
                        }

                        itemId = metaData.EntityPrototype.ID;
                    }

                    if (itemId != recipeSolid.Key)
                    {
                        continue;
                    }

                    if (stackComp is not null)
                    {
                        if (stackComp.Count == 1)
                        {
                            _container.Remove(item, ent.Comp.Storage);
                        }

                        _stack.ReduceCount((item, stackComp), 1);
                        break;
                    }
                    else
                    {
                        _container.Remove(item, ent.Comp.Storage);
                        Del(item);
                        break;
                    }
                }
            }
        }
    }

    public static bool CanSatisfyRecipe(FoodRecipePrototype recipe,
        Dictionary<string, int> solids,
        Dictionary<string, FixedPoint2> reagents)
    {
        foreach (var (id, needed) in recipe.IngredientsSolids)
        {
            if (!solids.TryGetValue(id, out var have) || have < needed)
                return false;
        }

        foreach (var (id, needed) in recipe.IngredientsReagents)
        {
            if (!reagents.TryGetValue(id, out var have) || have < needed)
                return false;
        }

        return true;
    }

    private (Dictionary<string, int>, Dictionary<string, FixedPoint2>) CollectIngredients(Entity<CookwareComponent> ent)
    {
        var solids = new Dictionary<string, int>();
        var reagents = new Dictionary<string, FixedPoint2>();
        if (!HasContents(ent.Comp))
            return (solids, reagents);

        foreach (var item in ent.Comp.Storage.ContainedEntities.ToArray())
        {
            // dum dum melt plastic
            if (_tag.HasTag(item, PlasticTag))
            {
                var junk = Spawn(ent.Comp.BadRecipeEntityId, Transform(ent).Coordinates);
                _container.Insert(junk, ent.Comp.Storage);
                Del(item);
                continue;
            }

            if (!HasComp<BeingCookedComponent>(ent))
            {
                var beingCookedComponent = AddComp<BeingCookedComponent>(ent);
                beingCookedComponent.Cookware = ent;
            }

            string? solidId = null;
            var amountToAdd = 1;

            if (TryComp<StackComponent>(item, out var stackComp))
            {
                solidId = _prototype.Index<StackPrototype>(stackComp.StackTypeId).Spawn;
                amountToAdd = stackComp.Count;
            }
            else
            {
                var metaData = MetaData(item);
                if (metaData.EntityPrototype is not null)
                    solidId = metaData.EntityPrototype.ID;
            }

            if (solidId is null)
                continue;

            if (!solids.TryAdd(solidId, amountToAdd))
                solids[solidId] += amountToAdd;
        }

        if (_solutionContainer.TryGetDrainableSolution(ent.Owner, out var _, out var solution))
        {
            foreach (var (reagent, quantity) in solution.Contents)
            {
                if (!reagents.TryAdd(reagent.Prototype, quantity))
                    reagents[reagent.Prototype] += quantity;
            }
        }

        return (solids, reagents);
    }

    private void OnReactionAttempt(Entity<BeingCookedComponent> ent, ref SolutionRelayEvent<ReactionAttemptEvent> args)
    {
        if (!TryComp<ActiveCookwareComponent>(ent.Comp.Cookware, out var activeCookwareComponent))
            return;

        if (activeCookwareComponent.Recipe == null) // no recipe selected
            return;

        var recipeReagents = activeCookwareComponent.Recipe.IngredientsReagents.Keys;

        foreach (var reagent in recipeReagents)
        {
            if (args.Event.Reaction.Reactants.ContainsKey(reagent))
            {
                args.Event.Cancelled = true;
                return;
            }
        }
    }

    private void OnConstructionTemp(Entity<BeingCookedComponent> ent, ref OnConstructionTemperatureEvent args)
    {
        args.Result = HandleResult.False;
    }

    private void OnActiveCookwareInsert(Entity<ActiveCookwareComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!TryComp<CookwareComponent>(ent, out var cookwareComponent))
            return;
        StopCooking((ent, cookwareComponent, ent.Comp));
    }

    private void OnActiveCookwareRemove(Entity<ActiveCookwareComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!TryComp<CookwareComponent>(ent, out var cookwareComponent))
            return;
        RemCompDeferred<BeingCookedComponent>(args.Entity);
        StopCooking((ent, cookwareComponent, ent.Comp));
    }

    private void OnInteractUsing(Entity<CookwareComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<ItemComponent>(args.Used, out var item))
        {
            if (_item.GetSizePrototype(item.Size) > _item.GetSizePrototype(ent.Comp.MaxItemSize))
            {
                _popupSystem.PopupEntity("It's too big to fit in!", ent, args.User);
            }
        }
        else
        {
            _popupSystem.PopupEntity("It won't work", ent, args.User);
            return;
        }

        if (ent.Comp.Storage.Count >= ent.Comp.Capacity)
        {
            _popupSystem.PopupEntity("It's full!", ent, args.User);
            return;
        }

        if (HasComp<DrainableSolutionComponent>(args.Used))
            return;

        args.Handled = true;
        _handsSystem.TryDropIntoContainer(args.User, args.Used, ent.Comp.Storage);
        UpdateUserInterfaceState(ent);
    }

    private void AddVerbs(Entity<CookwareComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || args.Hands == null)
            return;

        var @event = args;
        var possibleItem = _handsSystem.GetActiveItem(@event.User);
        if (possibleItem is not { } item)
            return;
        args.Verbs.Add(new AlternativeVerb()
        {
            Text = "Insert item",
            Category = VerbCategory.Insert,
            Act = () =>
            {
                _handsSystem.TryDropIntoContainer(@event.User, item, ent.Comp.Storage);
            },
            Priority = 1
        });

    }

    private void
        OnContentUpdate(EntityUid uid,
            CookwareComponent component,
            ContainerModifiedMessage args) // TODO: Replace with Entity<T> once works
    {
        if (component.Storage != args.Container)
            return;

        UpdateUserInterfaceState((uid, component));
    }

    private void OnInit(Entity<CookwareComponent> ent, ref ComponentInit args)
    {
        // this really does have to be in ComponentInit
        ent.Comp.Storage = _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
    }

    public void UpdateUserInterfaceState(Entity<CookwareComponent> ent)
    {
        _userInterface.SetUiState(ent.Owner,
            CookwareUiKey.Key,
            new CookwareUpdateUserInterfaceState(GetNetEntityArray(ent.Comp.Storage.ContainedEntities.ToArray())));
    }

    public static bool HasContents(CookwareComponent component)
    {
        return component.Storage.ContainedEntities.Any();
    }

    #region ui

    private void OnEjectMessage(Entity<CookwareComponent> ent, ref CookwareEjectMessage args)
    {
        if (!HasContents(ent.Comp))
            return;

        _container.EmptyContainer(ent.Comp.Storage);
        UpdateUserInterfaceState(ent);
    }

    private void OnEjectIndex(Entity<CookwareComponent> ent, ref CookwareEjectSolidIndexedMessage args)
    {
        if (!HasContents(ent.Comp))
            return;

        _container.Remove(GetEntity(args.EntityId), ent.Comp.Storage);
        UpdateUserInterfaceState(ent);
    }

    #endregion
}
