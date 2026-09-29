using EFT.InventoryLogic;
using LootingBots.Components;
using LootingBots.Utilities;

namespace LootingBots.Actions;

/// <summary>
/// Execute looting a list of items
/// </summary>
public class LootingLootAction : LootingAction
{
    private static readonly UnityEngine.Pool.ObjectPool<LootingLootAction> _pool = new(
        () => new LootingLootAction(),
        null,
        a => a.Reset(),
        ListActionPool.LogOnDestroyInstance,
        false,
        2,
        32
    );

    public static LootingLootAction Rent(Item itemToLoot, LootingInventoryController controller)
    {
        var moveAction = _pool.Get();

        moveAction.ItemsToLoot = UnityEngine.Pool.ListPool<Item>.Get();
        moveAction.ItemsToLoot.Add(itemToLoot);
        moveAction.LootingController = controller;
        // moveAction.NetWorthDelta = No need to set, we're using TryAddItemsToBotAsync which already adds the item's value to Stats

        return moveAction;
    }

    public static LootingLootAction Rent(List<Item> itemsToLoot, LootingInventoryController controller)
    {
        var moveAction = _pool.Get();

        moveAction.ItemsToLoot = UnityEngine.Pool.ListPool<Item>.Get();
        foreach (var itemToLoot in itemsToLoot)
        {
            moveAction.ItemsToLoot.Add(itemToLoot);
        }
        moveAction.LootingController = controller;
        // moveAction.NetWorthDelta = No need to set, we're using TryAddItemsToBotAsync which already adds the item's value to Stats

        return moveAction;
    }

    /// <summary>
    /// List of items to loot
    /// </summary>
    private List<Item> ItemsToLoot { get; set; }

    private LootingInventoryController LootingController { get; set; }

    public override Task<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token)
    {
        return LootingController.TryAddItemsToBotAsync(ItemsToLoot, token);
    }

    public override void Return()
    {
        _pool.Release(this);
    }

    protected override void Reset()
    {
        base.Reset();
        UnityEngine.Pool.ListPool<Item>.Release(ItemsToLoot);
        ItemsToLoot = null;
        LootingController = null;
    }
}
