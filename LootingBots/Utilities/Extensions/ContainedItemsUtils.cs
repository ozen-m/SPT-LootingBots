using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class ContainedItemsUtils
{
    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    /// <param name="templateId">Optional template ID to match a specific item template.</param>
    /// <param name="preAllocatedList">Pre-allocated list for all grid items found.</param>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem>(
        this InventoryController inventoryController,
        List<TItem> preAllocatedList
    )
        where TItem : Item
    {
        inventoryController.GetAllGridItemsInStorageSlotsNonAlloc<TItem, object>(preAllocatedList, null, null);
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem, TState>(
        this InventoryController inventoryController,
        List<TItem> preAllocatedList,
        Func<TItem, TState, bool> predicate,
        TState state
    )
        where TItem : Item
    {
        inventoryController.Inventory.Equipment.GetAllGridItemsInStorageSlotsNonAlloc(preAllocatedList, predicate, state);
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem>(this InventoryEquipment equipment, List<TItem> preAllocatedList)
        where TItem : Item
    {
        equipment.GetAllGridItemsInStorageSlotsNonAlloc<TItem, object>(preAllocatedList, null, null);
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem, TState>(
        this InventoryEquipment equipment,
        List<TItem> preAllocatedList,
        Func<TItem, TState, bool> predicate,
        TState state
    )
        where TItem : Item
    {
        foreach (var slotName in LootUtils.StorageSlots)
        {
            equipment.GetSlot(slotName).ContainedItem.GetAllGridContainedItems(preAllocatedList, predicate, state);
        }
    }

    /// <summary>
    /// Gets all contained items in all grids of an item and its children.
    /// </summary>
    /// <param name="item">The item whose grids are searched.</param>
    /// <param name="preAllocatedList">Pre-allocated list for all grid items found.</param>
    public static void GetAllGridContainedItems<TItem>(this Item item, List<TItem> preAllocatedList)
        where TItem : Item
    {
        item.GetAllGridContainedItems<TItem, object>(preAllocatedList, null, null);
    }

    /// <summary>
    /// Gets all contained items in all grids of an item and its children.
    /// </summary>
    /// <param name="item">The item whose grids are searched.</param>
    /// <param name="preAllocatedList">Pre-allocated list for all grid items found.</param>
    /// <param name="predicate">A predicate that determines whether the item is added.</param>
    /// <param name="state">The state passed to <paramref name="predicate"/> for each item.</param>
    public static void GetAllGridContainedItems<TItem, TState>(
        this Item item,
        List<TItem> preAllocatedList,
        Func<TItem, TState, bool> predicate,
        TState state
    )
        where TItem : Item
    {
        if (item is not CompoundItem compoundItem)
        {
            return;
        }

        foreach (var grid in compoundItem.Grids)
        {
            foreach (var containedItem in grid.ItemCollection.ItemsList)
            {
                if (containedItem is TItem tItem && (predicate is null || predicate(tItem, state)))
                {
                    preAllocatedList.Add(tItem);
                }
                containedItem.GetAllGridContainedItems(preAllocatedList, predicate, state);
            }
        }
    }

    /// <summary>
    /// Gets all contained items in all slots of an item and its children.
    /// </summary>
    /// <param name="item">The item whose slots are searched.</param>
    /// <param name="predicate">A predicate that determines whether the item is added.</param>
    /// <param name="state">The state passed to <paramref name="predicate"/> for each item.</param>
    public static void GetAllSlotContainedItems<TItem>(this Item item, List<TItem> preAllocatedList)
        where TItem : Item
    {
        item.GetAllSlotContainedItems<TItem, object>(preAllocatedList, null, null);
    }

    /// <summary>
    /// Gets all contained items in all slots of an item and its children.
    /// </summary>
    /// <param name="item">The item whose slots are searched.</param>
    /// <param name="predicate">A predicate that determines whether the item is added.</param>
    /// <param name="state">The state passed to <paramref name="predicate"/> for each item.</param>
    public static void GetAllSlotContainedItems<TItem, TState>(
        this Item item,
        List<TItem> preAllocatedList,
        Func<TItem, TState, bool> predicate,
        TState state
    )
        where TItem : Item
    {
        if (item is not CompoundItem compoundItem)
        {
            return;
        }

        foreach (var slot in compoundItem.Slots)
        {
            var containedItem = slot.ContainedItem;
            if (containedItem is TItem tItem && (predicate is null || predicate(tItem, state)))
            {
                preAllocatedList.Add(tItem);
            }
            containedItem.GetAllSlotContainedItems(preAllocatedList, predicate, state);
        }
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    public static Item GetFirstGridItemInStorageSlotsNonAlloc<TState>(
        this InventoryController controller,
        Func<Item, TState, bool> predicate,
        TState state
    )
    {
        return controller.Inventory.Equipment.GetFirstGridItemInStorageSlotsNonAlloc(predicate, state);
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    public static Item GetFirstGridItemInStorageSlotsNonAlloc<TState>(
        this InventoryEquipment equipment,
        Func<Item, TState, bool> predicate,
        TState state
    )
    {
        foreach (var slotName in LootUtils.StorageSlots)
        {
            var foundItem = equipment.GetSlot(slotName).ContainedItem.GetFirstGridContainedItem(predicate, state);
            if (foundItem is not null)
            {
                return foundItem;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the first contained item in all grids of an item and its children.
    /// </summary>
    /// <param name="item">The item whose grids are searched.</param>
    /// <param name="predicate">A predicate that determines whether the item is added.</param>
    /// <param name="state">The state passed to <paramref name="predicate"/> for each item.</param>
    public static Item GetFirstGridContainedItem<TState>(this Item item, Func<Item, TState, bool> predicate, TState state)
    {
        if (item is not CompoundItem compoundItem)
        {
            return null;
        }

        foreach (var grid in compoundItem.Grids)
        {
            foreach (var containedItem in grid.ItemCollection.ItemsList)
            {
                if (containedItem is not null && (predicate is null || predicate(containedItem, state)))
                {
                    return containedItem;
                }
                containedItem.GetFirstGridContainedItem(predicate, state);
            }
        }

        return null;
    }
}
