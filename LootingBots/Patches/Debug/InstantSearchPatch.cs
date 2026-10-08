using System.Reflection;
using EFT.InventoryLogic.Operations;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LootingBots.Patches.Debug;

[DebugPatch]
public class InstantSearchPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveSearchContentOperation), nameof(ActiveSearchContentOperation.Open));
    }

    [PatchPrefix]
    public static void PatchPrefix(ActiveSearchContentOperation __instance)
    {
        __instance.Instant = true;
    }
}
