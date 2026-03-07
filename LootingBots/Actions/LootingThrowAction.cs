using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;
using LootingBots.Components;
using LootingBots.Utilities;
using UnityEngine.Pool;

namespace LootingBots.Actions;

/// <summary>
/// Throw action to be executed
/// </summary>
public class LootingThrowAction : LootingAction
{
    private static readonly ObjectPool<LootingThrowAction> _pool = new(
        Create,
        null,
        a => a.Reset(),
        ListActionPool.LogOnDestroyInstance,
        true,
        32
    );

    public static LootingThrowAction Create()
    {
        return new LootingThrowAction();
    }

    public static LootingThrowAction Rent(Item item, float netWorthDelta = 0f)
    {
        var throwAction = _pool.Get();
        throwAction.Item = item;
        throwAction.NetWorthDelta = netWorthDelta;

        return throwAction;
    }

    public override UniTask<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token)
    {
        return controller.ThrowItemAsync(Item, token);
    }

    public override void Return()
    {
        _pool.Release(this);
    }
}
