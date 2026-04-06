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
        return typeof(BotsController).GetMethod(nameof(BotsController.BotDied), BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPrefix]
    private static void PatchPrefix(BotOwner botOwner)
    {
        if (botOwner.GetPlayer.TryGetComponent<LootingBrain>(out var lootingBrain))
        {
            UnityEngine.Object.Destroy(lootingBrain);
        }
        else
        {
            LootingBots.LootLog.LogError($"Could not destroy LootingBrain for {botOwner.name}");
        }

        if (botOwner.GetPlayer.TryGetComponent<LootFinder>(out var lootFinder))
        {
            UnityEngine.Object.Destroy(lootFinder);
        }
        else
        {
            LootingBots.LootLog.LogError($"Could not destroy LootFinder for {botOwner.name}");
        }

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"Cleanup on ActiveLootCache for {botOwner.name}");
        }

        ActiveLootCache.Cleanup(botOwner);
        ActiveBotCache.Remove(botOwner);
    }
}
