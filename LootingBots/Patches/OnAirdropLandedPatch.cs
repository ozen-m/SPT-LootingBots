using System.Reflection;
using EFT.Airdrop;
using EFT.Interactive;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

public class OnAirdropLandedPatch : ModulePatch
{
    public static event Action<LootableContainer> OnAirdropLanded;

    // PlayLandingSound in ClientAirDrop is called after the airdrop is landed, making it perfect for us to hook into
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ClientAirDrop).GetMethod(nameof(ClientAirDrop.PlayLandingSound));
    }

    [PatchPostfix]
    public static void Postfix(ClientAirDrop __instance)
    {
        var lootableContainer = __instance._syncObject.GetComponentInChildren<LootableContainer>();
        OnAirdropLanded?.Invoke(lootableContainer);
    }
}
