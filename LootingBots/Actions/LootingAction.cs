using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;
using LootingBots.Components;

namespace LootingBots.Actions;

/// <summary>
/// Base looting action
/// </summary>
/// <param name="item">Item operated upon</param>
/// <param name="netWorthDelta">Value added to the bot's net worth</param>
public abstract class LootingAction(
    Item item,
    float netWorthDelta = 0f
    )
{
    public Item Item { get; set; } = item;
    public float NetWorthDelta { get; set; } = netWorthDelta;

    public abstract UniTask<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token);
}
