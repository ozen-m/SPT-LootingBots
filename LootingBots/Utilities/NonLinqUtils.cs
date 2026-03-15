using EFT.InventoryLogic;

namespace LootingBots.Utilities;

public static class NonLinqUtils
{
    public static bool IsChangingWeaponNonLinq(this InventoryController controller)
    {
        foreach (var activeEvent in controller.List_0)
        {
            if (activeEvent is GEventArgs10 or GEventArgs9)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAnyHandsActionNonLinq(this TraderControllerClass controller)
    {
        foreach (var eventArg in controller.List_0)
        {
            if (eventArg is GInterface418)
            {
                return true;
            }
        }

        return false;
    }
}
