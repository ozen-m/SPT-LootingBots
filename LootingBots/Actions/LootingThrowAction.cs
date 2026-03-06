using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;
using LootingBots.Components;

namespace LootingBots.Actions;

/// <summary>
/// Throw action to be executed
/// </summary>
/// <param name="item">Item to dispose</param>
/// <param name="netWorthDelta">Value added to the bot's net worth, double check negative sign!</param>
public class LootingThrowAction(
    Item item,
    float netWorthDelta = 0f
) : LootingAction(item, netWorthDelta)
{
    public override async UniTask<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token)
    {
        return await controller.ThrowItemAsync(Item, token);
    }
}
