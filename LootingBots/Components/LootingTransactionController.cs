using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
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

        return MoveItemAsync(item, ableToEquip, token);
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

            return MergeItemAsync(item, mergeableItem, token);
        }

        // Otherwise, find an empty grid slot to put the item in
        var gridAddress = inventoryController.FindGridToPickUp(item);

        if (gridAddress != null && !string.Equals(gridAddress.GetRootItem()?.Parent?.Container?.ID, "securedcontainer", StringComparison.OrdinalIgnoreCase))
        {
            if (log.WarningEnabled)
            {
                log.LogWarning($"Picking up: {item.Name.Localized()} [place: {gridAddress.GetRootItem()?.Name.Localized()}]");
            }

            return MoveItemAsync(item, gridAddress, token);
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"No valid slot found for: {item.Name.Localized()}");
        }

        return UniTask.FromResult(false);
    }

    /// <summary>
    /// Moves an item to a specified item address
    /// </summary>
    public async UniTask<bool> MoveItemAsync(Item item, ItemAddress location, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (location == null)
        {
            return await TryEquipItemAsync(item, token);
        }

        if (log.WarningEnabled)
        {
            log.LogWarning($"Moving {item.Name.Localized()} to: {location.Container.ID.Localized()} [{location.GetRootItem()?.Name.Localized()}]...");
        }

        await SimulatePlayerDelayAsync(token: token);

        var moveResult = InteractionsHandlerClass.Move(item, location, inventoryController, true);
        if (moveResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogWarning($"Failed to move {item.Name.Localized()} to {location.Container.ID.Localized()} [{location.GetRootItem()?.Name.Localized()}]. Error: {moveResult.Error}");
            }
            return false;
        }

        var moveNetworkResult = await inventoryController.TryRunNetworkTransaction(moveResult).AsUniTask().AttachExternalCancellation(token);
        if (token.IsCancellationRequested || moveNetworkResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to move {item.Name.Localized()} to {location.Container.ID.Localized()} [{location.GetRootItem()?.Name.Localized()}]. Network Error: {moveNetworkResult?.Error}");
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"Moving {item.Name.Localized()} to: {location.Container.ID.Localized()} [{location.GetRootItem()?.Name.Localized()}]...done");
        }

        return true;
    }

    /** Moves an item to a specified item address. Supports executing a callback */
    public async UniTask<bool> SwapItemsAsync(Item item, Item toSwap, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (log.WarningEnabled)
        {
            log.LogWarning($"Swapping {item.Name.Localized()} with {toSwap.Name.Localized()}...");
        }

        await SimulatePlayerDelayAsync(token: token);

        var swapResult = InteractionsHandlerClass.Swap(item, toSwap.CurrentAddress, toSwap, item.CurrentAddress, inventoryController, true);
        if (swapResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to swap {item.Name.Localized()} with {toSwap.Name.Localized()}. Error: {swapResult.Error}");
            }
            return false;
        }

        var swapNetworkResult = await inventoryController.TryRunNetworkTransaction(swapResult).AsUniTask().AttachExternalCancellation(token);
        if (token.IsCancellationRequested || swapNetworkResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to swap {item.Name.Localized()} with {toSwap.Name.Localized()}. Network Error: {swapNetworkResult?.Error}");
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogWarning($"Swapping {item.Name.Localized()} with {toSwap.Name.Localized()}...done");
        }

        return true;
    }

    /** Attempts to merge an item stack with another specified item stack. Supports executing a callback */
    public async UniTask<bool> MergeItemAsync(Item toMove, Item toItem, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (toItem == null)
        {
            log.LogWarning($"Cannot merge item {toMove} to NULL target item!");
            return false;
        }

        if (log.WarningEnabled)
        {
            log.LogWarning($"Merging {toMove.Name?.Localized()} (Stack Size: {toMove.StackObjectsCount}) with: {toItem.Name.Localized()} (Stack Size: {toItem.StackObjectsCount})...");
        }

        var mergeResult = InteractionsHandlerClass.Merge(toMove, toItem, inventoryController, true);
        if (mergeResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to merge {toMove.Name.Localized()} (Stack Size: {toMove.StackObjectsCount}) with: {toItem.Name.Localized()} (Stack Size: {toItem.StackObjectsCount}). Error: {mergeResult.Error}" );
            }
            return false;
        }

        await SimulatePlayerDelayAsync(token: token);
        var mergeNetworkResult = await inventoryController.TryRunNetworkTransaction(mergeResult).AsUniTask().AttachExternalCancellation(token);
        if (token.IsCancellationRequested || mergeNetworkResult.Failed)
        {
            if (log.ErrorEnabled)
            {
                log.LogError($"Failed to merge {toMove.Name.Localized()} (Stack Size: {toMove.StackObjectsCount}) with: {toItem.Name.Localized()} (Stack Size: {toItem.StackObjectsCount}). Network Error: {mergeNetworkResult?.Error}" );
            }
            return false;
        }

        if (log.DebugEnabled)
        {
            log.LogDebug($"Merging {toMove.Name?.Localized()} (Stack Size: {toMove.StackObjectsCount}) with: {toItem.Name.Localized()} (Stack Size: {toItem.StackObjectsCount})...done");
        }

        return true;
    }

    /// <summary>
    /// Method used when we want the bot the throw an item
    /// </summary>
    public async UniTask<bool> ThrowItemAsync(Item toThrow, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (log.WarningEnabled)
        {
            log.LogWarning($"Throwing item: {toThrow.Name.Localized()}...");
        }

        await SimulatePlayerDelayAsync(token: token);

        var promise = new UniTaskCompletionSource<IResult>();
        inventoryController.ThrowItem(
            toThrow,
            false,
            result => promise.TrySetResult(result)
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
}
