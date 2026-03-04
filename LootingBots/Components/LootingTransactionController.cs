using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Actions;
using LootingBots.Utilities;
using InventoryControllerResultStruct = GStruct153;

namespace LootingBots.Components;

public class LootingTransactionController(InventoryController inventoryController, BotLog log)
{
    /** Tries to add extra spare ammo for the weapon being looted into the bot's secure container so that the bots are able to refill their mags properly in their reload logic */
    public bool AddExtraAmmo(Weapon weapon)
    {
        try
        {
            SearchableItemItemClass secureContainer = (SearchableItemItemClass)
                inventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem;

            StashGridClass container = secureContainer.Grids.FirstOrDefault();

            // Try to get the current ammo used by the weapon by checking the contents of the magazine. If its empty, try to create an instance of the ammo using the Weapon's CurrentAmmoTemplate
            Item ammoToAdd =
                weapon.GetCurrentMagazine()?.FirstRealAmmo()
                ?? Singleton<ItemFactoryClass>.Instance.CreateItem(MongoID.Generate(), weapon.CurrentAmmoTemplate._id, null);

            // Check to see if there already is ammo that meets the weapon's caliber in the secure container
            bool alreadyHasAmmo = false;

            foreach (var item in secureContainer.GetAllItems())
            {
                if (item is AmmoItemClass bullet && bullet.Caliber.Equals(((AmmoItemClass) ammoToAdd).Caliber))
                {
                    alreadyHasAmmo = true;
                    break; // Early exit as soon as a match is found
                }
            }

            // If we dont have any ammo, attempt to add 10 max ammo stacks into the bot's secure container for use in the bot's internal reloading code
            if (!alreadyHasAmmo)
            {
                if (log.DebugEnabled)
                {
                    log.LogDebug($"Trying to add ammo");
                }

                int ammoAdded = 0;

                for (int i = 0; i < 10; i++)
                {
                    Item ammo = ammoToAdd.CloneItem();
                    ammo.StackObjectsCount = ammo.StackMaxSize;

                    LocationInGrid location = container.FindFreeSpace(ammo);

                    if (location != null)
                    {
                        GStruct154<GClass3415> result = container.AddItemWithoutRestrictions(ammo, location);
                        if (result.Succeeded)
                        {
                            ammoAdded += ammo.StackObjectsCount;
                        }
                        else if (log.ErrorEnabled)
                        {
                            log.LogError($"Failed to add {ammo.Name.Localized()} to secure container");
                        }
                    }
                    else if (log.ErrorEnabled)
                    {
                        log.LogError($"Cannot find location in secure container for {ammo.Name.Localized()}");
                    }
                }

                if (ammoAdded > 0 && log.DebugEnabled)
                {
                    log.LogDebug($"Successfully added {ammoAdded} round of {ammoToAdd.Name.Localized()}");
                }
            }
            else if (log.DebugEnabled)
            {
                log.LogDebug($"Already has ammo for {weapon.Name.Localized()}");
            }
        }
        catch (Exception e)
        {
            log.LogError(e);
            return false;
        }

        return true;
    }

    /** Tries to find an open Slot to equip the current item to. If a slot is found, issue a move action to equip the item */
    public UniTask<bool> TryEquipItemAsync(Item item, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // Check to see if we can equip the item
        var ableToEquip = inventoryController.FindSlotToPickUp(item);
        if (ableToEquip == null)
        {
            if (log.DebugEnabled)
            {
                log.LogDebug($"Could not find a place to equip: {item.Name.Localized()}");
            }
            return UniTask.FromResult(false);
        }

        if (log.WarningEnabled)
        {
            log.LogWarning($"Equipping: {item.Name.Localized()} [place: {ableToEquip.Container.ID.Localized()}]");
        }

        return MoveItemAsync(new LootingMoveAction(item, ableToEquip), token);
    }

    /** Tries to find a valid grid for the item being looted. Checks all containers currently equipped to the bot. If there is a valid grid to place the item inside of, issue a move action to pick up the item */
    public UniTask<bool> TryPickupItemAsync(Item item, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // Check to see if this is an item that we can merge with another item in the inventory
        var mergeableItem = inventoryController.FindItemToMerge(item);

        if (mergeableItem != null)
        {
            if (log.WarningEnabled)
            {
                log.LogWarning($"Merging: {item.Name.Localized()} [with: {mergeableItem.Name.Localized()}]");
            }

            return MergeItemAsync(new LootingMoveAction(item, null, mergeableItem), token);
        }

        // Otherwise, find an empty grid slot to put the item in
        var gridAddress = inventoryController.FindGridToPickUp(item);

        if (gridAddress != null && !string.Equals(gridAddress.GetRootItem()?.Parent?.Container?.ID, "securedcontainer", StringComparison.OrdinalIgnoreCase))
        {
            if (log.WarningEnabled)
            {
                log.LogWarning($"Picking up: {item.Name.Localized()} [place: {gridAddress.GetRootItem()?.Name.Localized()}]");
            }

            return MoveItemAsync(new LootingMoveAction(item, gridAddress), token);
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"No valid slot found for: {item.Name.Localized()}");
        }

        return UniTask.FromResult(false);
    }

    /** Moves an item to a specified item address. Supports executing a callback */
    public async UniTask<bool> MoveItemAsync(LootingMoveAction moveAction, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (moveAction.Place == null)
        {
            log.LogWarning($"Cannot move item {moveAction.ToMove} to NULL place!");
            return false;
        }

        // if (moveAction.ToMove is Weapon weapon && moveAction.ToMove is not AmmoItemClass)
        // {
        //     //Todo: Doesn't work on Fika for obvious reasons
        //     //AddExtraAmmo(weapon);
        // }

        if (log.WarningEnabled)
        {
            log.LogWarning($"Moving {moveAction.ToMove.Name.Localized()} to: {moveAction.Place.Container.ID.Localized()}...");
        }

        var moveActionResult = InteractionsHandlerClass.Move(moveAction.ToMove, moveAction.Place, inventoryController, true);
        if (moveActionResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogWarning($"Failed to move {moveAction.ToMove.Name.Localized()} to {moveAction.Place.Container.ID.Localized()}. Error: {moveActionResult.Error}");
            }
            return false;
        }

        await SimulatePlayerDelayAsync(token: token);
        var moveActionNetworkResult = await inventoryController.TryRunNetworkTransaction(moveActionResult).AsUniTask();
        if (moveActionNetworkResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to move {moveAction.ToMove.Name.Localized()} to {moveAction.Place.Container.ID.Localized()}. Network Error: {moveActionNetworkResult.Error}");
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"Moving {moveAction.ToMove.Name.Localized()} to: {moveAction.Place.Container.ID.Localized()}...done");
        }

        if (moveAction.Callback != null)
        {
            await SimulatePlayerDelayAsync(token: token);
            await moveAction.Callback(token);
        }

        if (moveAction.OnComplete != null)
        {
            await SimulatePlayerDelayAsync(token: token);
            await moveAction.OnComplete(token);
        }

        return true;
    }

    /** Attempts to merge an item stack with another specified item stack. Supports executing a callback */
    public async UniTask<bool> MergeItemAsync(LootingMoveAction moveAction, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (moveAction.ToItem == null)
        {
            log.LogWarning($"Cannot merge item {moveAction.ToMove} to NULL target item!");
            return false;
        }

        if (log.WarningEnabled)
        {
            log.LogWarning($"Merging {moveAction.ToMove.Name?.Localized()} (Stack Size: {moveAction.ToMove.StackObjectsCount}) with: {moveAction.ToItem.Name.Localized()} (Stack Size: {moveAction.ToItem.StackObjectsCount})...");
        }

        var mergeResult = InteractionsHandlerClass.Merge(moveAction.ToMove, moveAction.ToItem, inventoryController, true);
        if (mergeResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to merge {moveAction.ToMove.Name.Localized()} (Stack Size: {moveAction.ToMove.StackObjectsCount}) with: {moveAction.ToItem.Name.Localized()} (Stack Size: {moveAction.ToItem.StackObjectsCount}). Error: {mergeResult.Error}" );
            }
            return false;
        }

        await SimulatePlayerDelayAsync(token: token);
        var mergeNetworkResult = await inventoryController.TryRunNetworkTransaction(mergeResult).AsUniTask();
        if (mergeNetworkResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to merge {moveAction.ToMove.Name.Localized()} (Stack Size: {moveAction.ToMove.StackObjectsCount}) with: {moveAction.ToItem.Name.Localized()} (Stack Size: {moveAction.ToItem.StackObjectsCount}). Network Error: {mergeNetworkResult.Error}" );
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"Merging {moveAction.ToMove.Name?.Localized()} (Stack Size: {moveAction.ToMove.StackObjectsCount}) with: {moveAction.ToItem.Name.Localized()} (Stack Size: {moveAction.ToItem.StackObjectsCount})...done");
        }

        if (moveAction.Callback != null)
        {
            await SimulatePlayerDelayAsync(token: token);
            await moveAction.Callback(token);
        }

        if (moveAction.OnComplete != null)
        {
            await SimulatePlayerDelayAsync(token: token);
            await moveAction.OnComplete(token);
        }

        return true;
    }

    /** Method used when we want the bot the throw an item and then equip an item immediately afterwards */
    public async UniTask<bool> ThrowAndEquipAsync(LootingSwapAction swapAction, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var toThrow = swapAction.ToThrow;
        if (log.WarningEnabled)
        {
            log.LogWarning($"Throwing item: {toThrow.Name.Localized()}...");
        }

        await SimulatePlayerDelayAsync(token: token);

        var promise = new UniTaskCompletionSource<IResult>();
        var swapCallback = swapAction.Callback;
        inventoryController.ThrowItem(
            toThrow,
            false,
            result =>
            {
                _ = SimulatedDelayCallbackAsync(result, swapCallback, promise, token);
            }
        );

        var throwResult = await promise.Task;
        if (throwResult.Failed)
        {
            if (log.WarningEnabled)
            {
                log.LogWarning($"Failed to throw item: {toThrow.Name.Localized()}. Error: {throwResult.Error}");
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"Throwing item: {toThrow.Name.Localized()}...done");
        }

        if (swapAction.OnComplete != null)
        {
            await swapAction.OnComplete(token);
        }

        return true;
    }

    public UniTask<IResult> TryRunNetworkTransactionAsync(InventoryControllerResultStruct operationResult, Callback callback = null)
    {
        return inventoryController.TryRunNetworkTransaction(operationResult, callback).AsUniTask();
    }

    public static UniTask SimulatePlayerDelayAsync(double delay = -1f, CancellationToken token = default)
    {
        if (delay == -1D)
        {
            delay = LootingBots.TransactionDelay.Value;
        }

        return UniTask.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken: token);
    }

    private static async UniTask SimulatedDelayCallbackAsync(IResult result, Func<CancellationToken, UniTask> callback, UniTaskCompletionSource<IResult> promise, CancellationToken token = default)
    {
        if (result.Succeed)
        {
            await SimulatePlayerDelayAsync(token: token);
            if (callback != null)
            {
                await callback(token);
            }
        }

        promise.TrySetResult(result);
    }
}
