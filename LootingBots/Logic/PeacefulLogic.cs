using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace LootingBots.Logic;

/// <summary>
/// PatrolAssault peaceful logic
/// </summary>
internal class PeacefulLogic(BotOwner botOwner) : CustomLogic(botOwner)
{
    private readonly PeacefulNode _baseLogic = new(botOwner);

    public override void Update(CustomLayer.ActionData data)
    {
        _baseLogic.UpdateNodeByBrain(data);
    }
}
