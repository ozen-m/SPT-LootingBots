using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LootingBots.Patches.Debug;

[DebugPatch]
public class SeeSecuredContainerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAllowedToSeeSlot));
    }

    [PatchPostfix]
    public static void PatchPostfix(InventoryController __instance, EquipmentSlot slotName, ref bool __result)
    {
        if (__result)
        {
            return;
        }

        if (slotName == EquipmentSlot.SecuredContainer && __instance is Player.SinglePlayerInventoryController)
        {
            __result = true;
        }
    }
}
