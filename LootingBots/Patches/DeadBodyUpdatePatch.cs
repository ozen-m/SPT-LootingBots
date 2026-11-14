using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

/// <summary>
/// Runs in layers <see cref="PatrolActionsLayer"/> and <see cref="FollowerPatrolLayer"/>.
/// Won't run in Utility peace anymore since layer is removed, but still runs in PatrolFollower.
/// </summary>
public class DeadBodyUpdatePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotDeadBodyWork).GetMethod(nameof(BotDeadBodyWork.UpdateCheck));
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpile()
    {
        return [new CodeInstruction(OpCodes.Ret)];
    }
}
