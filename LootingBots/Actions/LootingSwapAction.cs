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
    private static readonly UnityEngine.Pool.ObjectPool<LootingSwapAction> _pool = new(
        () => new LootingSwapAction(),
        null,
        a => a.Reset(),
        ListActionPool.LogOnDestroyInstance,
        false,
        2,
        32
    );

    public static LootingSwapAction Rent(Item item, Item toSwap, float netWorthDelta = 0f, bool transferItems = false)
    {
        var swapAction = _pool.Get();
        swapAction.Item = item;
        swapAction.ToSwap = toSwap;
        swapAction.NetWorthDelta = netWorthDelta;
        swapAction.TransferItems = transferItems;

        return swapAction;
    }

    /// <summary>
    /// Item to be swapped with
    /// </summary>
    public Item ToSwap { get; set; }

    /// <summary>
    /// Loot items from thrown item if true
    /// </summary>
    public bool TransferItems { get; set; }

    public override async Task<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token = default)
    {
        if (await controller.SwapItemsAsync(Item, ToSwap, token))
        {
            return true;
        }

        // Swap failed, try throwing first then equipping after.
        // Check if item can be equipped to ToSwap's address,
        // then rollback since we're not simulating
        var toSwapAddress = ToSwap.CurrentAddress;
        var inventoryController = ToSwap.Owner as InventoryController;
        var removeResult = ItemManipulator.Remove(ToSwap, inventoryController, false);
        var moveResult = ItemManipulator.Move(Item, toSwapAddress, inventoryController, false);

        moveResult.Value?.RollBack();
        removeResult.Value?.RollBack();

        if (moveResult.Failed)
        {
            return false;
        }

        // If throw-equip simulation was successful, run it
        return await controller.ThrowItemAsync(ToSwap, token) && await controller.TryEquipItemAsync(Item, token);
    }

    public override async Task PostActionsAsync(LootingInventoryController invController, CancellationToken token = default)
    {
        if (!TransferItems)
        {
            return;
        }

        if (ToSwap is Weapon thrownWeapon)
        {
            // If we swapped away our previous weapon, throw away its mags and strip the attachments
            using (DictionaryPool<Item, float>.Get(out var uselessMagazines))
            {
                await invController.ThrowUselessMagsAsync(thrownWeapon, uselessMagazines, token);
            }
            if (LootingBots.CanStripAttachments.Value)
            {
                using (UnityEngine.Pool.ListPool<Item>.Get(out var modsToLoot))
                {
                    await invController.StripWeaponAsync(thrownWeapon, modsToLoot, token);
                }
            }
            return;
        }

        // To make space we throw undervalued items in our newly equipped item
        using (DictionaryPool<Item, float>.Get(out var itemsToThrow))
        {
            invController.GetUndervaluedItems(Item, itemsToThrow);
            await invController.ThrowUndervaluedItemsAsync(Item, itemsToThrow, token);
        }

        // Clean up vest of other items
        if (Item is Vest newVest)
        {
            await invController.TransferItemsToBackpackAsync(newVest);
        }

        // Try to pick up any nested items in the old vest,
        // this helps to transfer ammo to the bots active rig
        if (ToSwap is Vest oldVest)
        {
            await invController.LootNestedItemsAsync(oldVest, token);
        }
    }

    public override void Return()
    {
        _pool.Release(this);
    }

    protected override void Reset()
    {
        base.Reset();
        ToSwap = null;
        TransferItems = false;
    }
}
