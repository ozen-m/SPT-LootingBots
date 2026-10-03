using EFT;
using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class ContainedItemsUtils
{
    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    /// <param name="templateId">Optional template ID to match a specific item template.</param>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem>(
        this InventoryController inventoryController,
        List<TItem> preAllocatedList,
        MongoID templateId = default
    )
        where TItem : Item
    {
        inventoryController.Inventory.Equipment.GetAllGridItemsInStorageSlotsNonAlloc(preAllocatedList, templateId);
    }

    /// <summary>
    /// Get all grid items from the equipment's storage slots.
    /// The SecuredContainer slot is excluded.
    /// </summary>
    /// <param name="templateId">Optional template ID to match a specific item template.</param>
    public static void GetAllGridItemsInStorageSlotsNonAlloc<TItem>(
        this InventoryEquipment equipment,
        List<TItem> preAllocatedList,
        MongoID templateId = default
    )
        where TItem : Item
    {
        foreach (var slotName in LootUtils.StorageSlots)
        {
            equipment.GetSlot(slotName).ContainedItem.GetAllGridContainedItems(preAllocatedList, templateId);
        }
    }

    /// <summary>
    /// Gets all contained items in all grids of an item and its children.
    /// </summary>
    /// <param name="item">The item whose grids are searched.</param>
    /// <param name="templateId">Optional template ID to match a specific item template.</param>
    public static void GetAllGridContainedItems<TItem>(this Item item, List<TItem> preAllocatedList, MongoID templateId = default)
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
                if (containedItem is TItem tItem && (templateId == default || tItem.TemplateId == templateId))
                {
                    preAllocatedList.Add(tItem);
                }
                containedItem.GetAllGridContainedItems(preAllocatedList, templateId);
            }
        }
    }

    /// <summary>
    /// Gets all contained items in all slots of an item and its children.
    /// </summary>
    /// <param name="item">The item whose slots are searched.</param>
    /// <param name="templateId">Optional template ID to match a specific item template.</param>
    public static void GetAllSlotContainedItems<TItem>(this Item item, List<TItem> preAllocatedList, MongoID? templateId = null)
        where TItem : Item
    {
        if (item is not CompoundItem compoundItem)
        {
            return;
        }

        foreach (var slot in compoundItem.Slots)
        {
            var containedItem = slot.ContainedItem;
            if (containedItem is TItem tItem && (templateId is null || tItem.TemplateId == templateId))
            {
                preAllocatedList.Add(tItem);
            }
            containedItem?.GetAllSlotContainedItems(preAllocatedList, templateId);
        }
    }
}
