using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LootingBots.Patches;

/// <summary>
/// Runs when a player is removed as an ally/enemy in <see cref="BotsGroup"/>. <see cref="BotDeadBodyWork"/> does not run with LB.
/// </summary>
public class DeadBodyAddBodyPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(DeadBodiesController).GetMethod(nameof(DeadBodiesController.AddBody), [typeof(IPlayer)]);
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpile()
    {
        return [new CodeInstruction(OpCodes.Ret)];
    }
}
