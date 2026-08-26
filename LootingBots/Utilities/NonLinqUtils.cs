using EFT.InventoryLogic;

namespace LootingBots.Utilities;

public static class NonLinqUtils
{
    public static bool IsChangingWeaponNonLinq(this InventoryController controller)
    {
        foreach (var activeEvent in controller.ActiveEvents)
        {
            if (activeEvent is RemoveFromHandsEventArgs or SetInHandsEventArgs)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAnyHandsActionNonLinq(this ItemController controller)
    {
        foreach (var eventArg in controller.ActiveEvents)
        {
            if (eventArg is IItemInHandsEventArgs)
            {
                return true;
            }
        }

        return false;
    }
}
