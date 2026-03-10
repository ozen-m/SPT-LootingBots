using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace LootingBots.Utilities;

public static class LootUtils
{
    public const int RESERVED_SLOT_COUNT = 2;
    public static readonly LayerMask LowPolyMask = LayerMask.GetMask(["LowPolyCollider"]);
    public static readonly LayerMask LootMask = LayerMask.GetMask(["Interactive", "Loot", "Deadbody"]);

    private static readonly EquipmentSlot[] WeaponSlots =
    [
        EquipmentSlot.Holster,
        EquipmentSlot.FirstPrimaryWeapon,
        EquipmentSlot.SecondPrimaryWeapon,
    ];

    private static readonly EquipmentSlot[] StorageSlots =
    [
        EquipmentSlot.Backpack,
        EquipmentSlot.ArmorVest,
        EquipmentSlot.TacticalVest,
        EquipmentSlot.Pockets,
    ];

    private static readonly EquipmentSlot[] OtherSlots =
    [
        EquipmentSlot.Headwear,
        EquipmentSlot.Earpiece,
        EquipmentSlot.Dogtag,
        EquipmentSlot.Scabbard,
        EquipmentSlot.FaceCover,
    ];

    /** Calculate the size of a container */
    public static int GetContainerSize(this SearchableItemItemClass container)
    {
        StashGridClass[] grids = container.Grids;
        int gridSize = 0;

        foreach (StashGridClass grid in grids)
        {
            gridSize += grid.GridHeight * grid.GridWidth;
        }

        return gridSize;
    }

    /// <summary>
    /// Checks if a key is a Single Use Item like the "Unknown Key"
    /// </summary>
    /// <param name="item">The item to check</param>
    /// <returns>returns true if it's single use, false otherwise</returns>
    public static bool IsSingleUseKey(this Item item)
    {
        KeyComponent key = item.GetItemComponent<KeyComponent>();
        return key != null && key.Template.MaximumNumberOfUsage == 1;
    }

    /** Triggers a container to open/close **/
    /** Borrowed from Questing Bots, needed for Fika **/
    public static void InteractContainer(WorldInteractiveObject worldInteractiveObject, BotOwner botOwner, EInteractionType action, BotLog log)
    {
        if (worldInteractiveObject == null)
        {
            if (log.DebugEnabled)
            {
                log.LogWarning($"Interacting [{action.ToString()}] with WorldInteractiveObject but is NULL");
            }
            return;
        }

        InteractionResult interactionResult = new InteractionResult(action);
        if (worldInteractiveObject is Door)
        {
            // NOTE: This method MUST be used for Fika compatibility
            botOwner.GetPlayer.vmethod_0(worldInteractiveObject, interactionResult, null);
        }

        // NOTE: This method MUST be used for Fika compatibility
        botOwner.GetPlayer.vmethod_1(worldInteractiveObject, interactionResult);
    }

    /**
    * Calculates the amount of empty grid slots in the container
    */
    public static int GetAvailableGridSlots(StashGridClass[] grids)
    {
        if (grids is null)
        {
            return 0;
        }

        // Initialize freeSpaces to 0
        int freeSpaces = 0;

        // Loop through each grid and calculate the free spaces
        foreach (StashGridClass grid in grids)
        {
            int gridSize = grid.GridHeight * grid.GridWidth;
            int containedItemSize = grid.GetSizeOfContainedItems();
            freeSpaces += gridSize - containedItemSize;
        }

        return freeSpaces;
    }

    /// <summary>
    /// returns the amount of space taken up by all the items in a given grid slot
    /// </summary>
    /// <param name="grid">The grid to calculate the amount of space taken up for</param>
    /// <returns>Returns the item size as an integer</returns>
    public static int GetSizeOfContainedItems(this StashGridClass grid)
    {
        int containedItemSize = 0;

        // Loop through each item in grid.Items and accumulate the item size
        foreach (Item item in grid.Items)
        {
            containedItemSize += item.GetItemSize();
        }

        return containedItemSize;
    }

    /// <summary>
    /// Get the size of an item in a grid
    /// </summary>
    /// <param name="item">The item to get the size for</param>
    public static int GetItemSize(this Item item)
    {
        var dimensions = item.CalculateCellSize();
        return dimensions.X * dimensions.Y;
    }

    /** Given an item that is stackable and can be merged, search through the inventory and find any matches of that item that are not in a secure container. */
    public static Item FindItemToMerge(this InventoryController controller, Item item)
    {
        // Return null if item cannot be stacked
        if (item.StackMaxSize <= 1)
        {
            return null;
        }

        // Use the item's template id to search for the same item in the inventory
        foreach (Item foundItem in controller.Inventory.GetAllItemByTemplate(item.TemplateId))
        {
            if (foundItem == null)
            {
                continue;
            }

            Item rootItem = foundItem.GetRootItem();

            // Do not try to merge with cartridges or weapon chambers
            if (foundItem.Parent.Container is StackSlot or Slot)
            {
                continue;
            }

            if (rootItem.Parent.Container.ID.Equals("securedcontainer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.StackObjectsCount + foundItem.StackObjectsCount <= foundItem.StackMaxSize)
            {
                return foundItem;
            }
        }

        return null;
    }

    /**
   *   Returns the list of slots to loot from a corpse in priority order. When a bot already has a backpack/rig, they will attempt to loot the weapons off the bot first. Otherwise they will loot the equipement first and loot the weapons afterwards.
   */
    public static void GetPriorityItems(this InventoryEquipment corpseEquipment, InventoryEquipment botEquipment, List<Item> preallocatedList)
    {
        bool hasBackpack = botEquipment.GetSlot(EquipmentSlot.Backpack).ContainedItem != null;
        bool hasTacVest = botEquipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem != null;

        // Add slots in priority order
        if (hasBackpack || hasTacVest)
        {
            GetItemInSlotsNonAlloc(corpseEquipment, botEquipment, preallocatedList, WeaponSlots);
            GetItemInSlotsNonAlloc(corpseEquipment, botEquipment, preallocatedList, StorageSlots);
        }
        else
        {
            GetItemInSlotsNonAlloc(corpseEquipment, botEquipment, preallocatedList, StorageSlots);
            GetItemInSlotsNonAlloc(corpseEquipment, botEquipment, preallocatedList, WeaponSlots);
        }

        GetItemInSlotsNonAlloc(corpseEquipment, botEquipment, preallocatedList, OtherSlots);
    }

    private static void GetItemInSlotsNonAlloc(InventoryEquipment equipment, InventoryEquipment botEquipment, List<Item> preallocatedList, EquipmentSlot[] slots)
    {
        var equipmentOwner = equipment.Parent.GetOwner();
        var botOwner = botEquipment.Parent.GetOwner();
        foreach (EquipmentSlot slotName in slots)
        {
            var slot =  equipment.GetSlot(slotName);
            var item = slot.ContainedItem;
            if (item == null)
            {
                continue;
            }

            // Check if item is unlootable
            var unlootableComponent = item.GetItemComponent<UnlootableComponent>();
            if (unlootableComponent != null &&
                equipmentOwner != botOwner &&
                unlootableComponent.IsUnlootableFrom(item.Parent.Container) &&
                item is not PocketsItemClass) // Include pockets to loot list
            {
                continue;
            }

            preallocatedList.Add(item);
        }
    }

    public static Item GetRootItem(this InteractableObject interactableObject)
    {
        return interactableObject switch
        {
            LootableContainer container => container.ItemOwner?.RootItem,
            LootItem lootItem => lootItem.ItemOwner?.RootItem,
            _ => null
        };
    }

    public static string GetRootItemId(this InteractableObject interactableObject)
    {
        return interactableObject switch
        {
            LootableContainer container => container.ItemOwner?.RootItem.Id,
            LootItem lootItem => lootItem.ItemOwner?.RootItem.Id,
            _ => null
        };
    }

    public static string GetLootName(this InteractableObject interactableObject)
    {
        return interactableObject switch
        {
            LootableContainer container => container.ItemOwner?.RootItem.Name.Localized(),
            Corpse corpse => corpse.name,
            LootItem lootItem => lootItem.ItemOwner?.RootItem.Name.Localized(),
            _ => "-"
        };
    }

    public static bool HasAnyHandsActionNonLinq(this TraderControllerClass controller)
    {
        foreach (var eventArg in controller.List_0)
        {
            if (eventArg is GInterface418)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if moving an item to a slot is blocked
    /// Except chest armor
    /// Based on Slot.method_3
    /// </summary>
    public static bool HasBlockingItem(this Slot slot, Item incomingItem)
    {
        var conflictingSlots = slot.ConflictingSlots;
        if (conflictingSlots is null)
        {
            return false;
        }

        if (!incomingItem.TryGetItemComponent<SlotBlockerComponent>(out var slotBlocker))
        {
            return false;
        }

        var slotNames = slotBlocker.ConflictingSlotNames;
        for (var i = 0; i < slotNames.Length; i++)
        {
            if (conflictingSlots.TryGetValue(slotNames[i], out var conflictingSlot) &&
                conflictingSlot != slot && // Exclude checking the same slot
                conflictingSlot.ContainedItem is {} conflictItem &&
                conflictItem is not ArmorItemClass and not VestItemClass) // Exclude chest armor
            {
                return true;
            }
        }

        return false;
    }
}
