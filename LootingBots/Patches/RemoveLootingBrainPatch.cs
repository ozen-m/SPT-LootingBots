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
        else if (BotHasLootingLayer(botOwner))
        {
            LootingBots.LootLog.LogError($"Could not destroy LootingBrain for {botOwner.name}");
        }

        if (botOwner.GetPlayer.TryGetComponent<LootFinder>(out var lootFinder))
        {
            UnityEngine.Object.Destroy(lootFinder);
        }
        else if (BotHasLootingLayer(botOwner))
        {
            LootingBots.LootLog.LogError($"Could not destroy LootFinder for {botOwner.name}");
        }

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"Cleanup on LB components for {botOwner.name}");
        }

        ActiveBotCache.Remove(botOwner);
    }

    private static bool BotHasLootingLayer(BotOwner botOwner)
    {
        foreach (var (_, layer) in botOwner.Brain.BaseBrain.Dictionary_0)
        {
            if (layer.Name() == "Looting")
            {
                return true;
            }
        }

        return false;
    }
}
