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
    private static readonly UnityEngine.Pool.ObjectPool<LootingThrowAction> _pool = new(
        () => new LootingThrowAction(),
        null,
        a => a.Reset(),
        ListActionPool.LogOnDestroyInstance,
        false,
        32
    );

    public static LootingThrowAction Rent(Item item, float netWorthDelta = 0f, bool transferItems = true)
    {
        var throwAction = _pool.Get();
        throwAction.Item = item;
        throwAction.NetWorthDelta = netWorthDelta;
        throwAction.TransferItems = transferItems;

        return throwAction;
    }

    /// <summary>
    /// Loot items from thrown item if true
    /// </summary>
    public bool TransferItems { get; set; }

    public override Task<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token = default)
    {
        return controller.ThrowItemAsync(Item, token);
    }

    public override async Task PostActionsAsync(LootingInventoryController invController, CancellationToken token = default)
    {
        if (!TransferItems)
        {
            return;
        }

        // Ignore thrown loot
        invController.IgnoreLoot(Item.Id);

        if (Item is Weapon thrownWeapon)
        {
            // Throw mags of thrown weapon and strip attachments
            using (DictionaryPool<Item, float>.Get(out var uselessMagazines))
            {
                await invController.ThrowUselessMagsAsync(thrownWeapon, uselessMagazines, token);
            }
            if (LootingBots.CanStripAttachments.Value)
            {
                using (UnityEngine.Pool.ListPool<Mod>.Get(out var modsToLoot))
                {
                    await invController.StripWeaponAsync(thrownWeapon, modsToLoot, token);
                }
            }
        }
        else if (Item is Vest oldVest)
        {
            // Try to pick up any nested items in the old vest,
            // this helps to transfer ammo to the bots active rig
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
        TransferItems = false;
    }
}
