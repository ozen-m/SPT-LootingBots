using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

public class InvokeOnKillPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocalPlayer).GetMethod(nameof(LocalPlayer.OnBeenKilledByAggressor));
    }

    [PatchPostfix]
    protected static void Postfix(
        LocalPlayer __instance,
        IPlayer aggressor,
        DamageInfoStruct damageInfo,
        EBodyPart bodyPart,
        EDamageType lethalDamageType
    )
    {
        // Skip if the aggressor is a human player
        var aggressorBotOwner = aggressor.AIData?.BotOwner;
        if (aggressorBotOwner == null)
        {
            return;
        }

        // Call KillTarget where OnKillTarget invokes which we can subscribe to,
        // for the aggressor to prioritize the victim's corpse
        aggressorBotOwner.BotPersonalStats.KillTarget(__instance.ProfileId, damageInfo);
    }
}
