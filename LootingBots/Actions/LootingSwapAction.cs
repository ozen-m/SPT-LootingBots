using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;
using LootingBots.Components;
using LootingBots.Utilities;
using UnityEngine.Pool;

namespace LootingBots.Actions;

/// <summary>
/// Swap action to be executed
/// </summary>
/// <inheritdoc/>
public class LootingSwapAction : LootingAction
{
    private static readonly ObjectPool<LootingSwapAction> _pool = new(
        Create,
        null,
        a => a.Reset(),
        ListActionPool.LogOnDestroyInstance,
        true,
        2,
        32
    );

    public static LootingSwapAction Create()
    {
        return new LootingSwapAction();
    }

    public static LootingSwapAction Rent(Item item, Item toSwap, float netWorthDelta = 0f, bool throwMags = false, bool transferItems = false)
    {
        var swapAction = _pool.Get();
        swapAction.Item = item;
        swapAction.ToSwap = toSwap;
        swapAction.NetWorthDelta = netWorthDelta;
        swapAction.ThrowMags = throwMags;
        swapAction.TransferItems = transferItems;

        return swapAction;
    }

    /// <summary>
    /// Item to be swapped with
    /// </summary>
    public Item ToSwap { get; set; }

    /// <summary>
    /// Throw unused magazines previously used by ToSwap
    /// </summary>
    public bool ThrowMags { get; set; }

    /// <summary>
    /// Loot items from thrown item if true
    /// </summary>
    public bool TransferItems { get; set; }

    public override async UniTask<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token)
    {
        return await controller.SwapItemsAsync(Item, ToSwap, token);
    }

    public override void Return()
    {
        _pool.Release(this);
    }

    protected override void Reset()
    {
        base.Reset();
        ToSwap = null;
        ThrowMags = false;
        TransferItems = false;
    }
}
