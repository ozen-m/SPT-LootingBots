using System.Reflection;
using EFT;
using LootingBots.Components;
using LootingBots.Utilities;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

public class RemoveLootingBrainPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotOwner).GetMethod(nameof(BotOwner.Dispose), BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPrefix]
    private static void PatchPrefix(BotOwner __instance)
    {
        if (__instance.GetPlayer.TryGetComponent<LootingBrain>(out var lootingBrain))
        {
            UnityEngine.Object.Destroy(lootingBrain);
        }

        if (__instance.GetPlayer.TryGetComponent<LootFinder>(out var lootFinder))
        {
            UnityEngine.Object.Destroy(lootFinder);
        }

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug("Cleanup on ActiveLootCache");
        }

        ActiveLootCache.Cleanup(__instance);
        ActiveBotCache.Remove(__instance);
    }
}
