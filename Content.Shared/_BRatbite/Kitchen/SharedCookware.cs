using Robust.Shared.Serialization;

namespace Content.Shared._BRatbite.Kitchen;

[Serializable, NetSerializable]
public sealed class CookwareEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CookwareEjectSolidIndexedMessage(NetEntity entityId) : BoundUserInterfaceMessage
{
    public NetEntity EntityId = entityId;
}

[NetSerializable, Serializable]
public sealed class CookwareUpdateUserInterfaceState( NetEntity[] containedSolids) : BoundUserInterfaceState
{
    public NetEntity[] ContainedSolids = containedSolids;
}

[NetSerializable, Serializable]
public enum CookwareUiKey
{
    Key
}
