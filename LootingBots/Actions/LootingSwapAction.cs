using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;
using LootingBots.Components;

namespace LootingBots.Actions;

/// <summary>
/// Swap action to be executed
/// </summary>
/// <param name="toSwap"><paramref name="item"/> will be switched to <paramref name="toSwap"/>'s address</param>
/// <param name="throwMags">Throw unused magazines previously used by <paramref name="toSwap"/></param>
/// <param name="transferItems">Loot items from thrown item if true</param>
/// <inheritdoc/>
public class LootingSwapAction(
    Item item,
    Item toSwap,
    float netWorthDelta = 0f,
    bool throwMags = false,
    bool transferItems = false
) : LootingAction(item, netWorthDelta)
{
    public Item ToSwap { get; set; } = toSwap;
    public bool ThrowMags { get; set; } = throwMags;
    public bool TransferItems { get; set; } = transferItems;

    public override async UniTask<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token)
    {
        return await controller.SwapItemsAsync(Item, ToSwap, token);
    }
}
