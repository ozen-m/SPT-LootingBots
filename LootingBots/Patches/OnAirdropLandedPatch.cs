using System.Reflection;
using EFT.Interactive;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

public class OnAirdropLandedPatch : ModulePatch
{
    public static Action<LootableContainer> OnAirdropLanded;

    // method_0 in AirdropLogicClass is called after the airdrop is landed, making it perfect for us to hook into
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AirdropLogicClass).GetMethod(nameof(AirdropLogicClass.method_0));
    }

    [PatchPostfix]
    public static void Postfix(AirdropLogicClass __instance)
    {
        var lootableContainer = __instance.AirdropSynchronizableObject_0.GetComponentInChildren<LootableContainer>();

        if (OnAirdropLanded != null)
        {
            OnAirdropLanded(lootableContainer);
        }
    }
}
