using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Grid = EFT.InventoryLogic.Grid;

namespace LootingBots.Utilities;

public static class LootUtils
{
    public const int RESERVED_SLOT_COUNT = 2;
    public static readonly int LowPolyMask = LayerMask.GetMask("LowPolyCollider");
    public static readonly int LootMask = LayerMask.GetMask("Interactive", "Loot", "Deadbody");
    public static readonly AccessTools.FieldRef<Player, Corpse> PlayerCorpseField = AccessTools.FieldRefAccess<Player, Corpse>("Corpse");

    public static readonly EquipmentSlot[] WeaponSlots =
    [
        EquipmentSlot.Holster,
        EquipmentSlot.FirstPrimaryWeapon,
        EquipmentSlot.SecondPrimaryWeapon,
    ];

    public static readonly EquipmentSlot[] StorageSlots =
    [
        EquipmentSlot.Backpack,
        EquipmentSlot.TacticalVest,
        EquipmentSlot.ArmorVest,
        EquipmentSlot.Pockets,
    ];

    public static readonly EquipmentSlot[] OtherSlots =
    [
        EquipmentSlot.ArmBand,
        EquipmentSlot.Headwear,
        EquipmentSlot.Earpiece,
        EquipmentSlot.Dogtag,
        EquipmentSlot.Scabbard,
        EquipmentSlot.FaceCover,
        EquipmentSlot.Eyewear,
    ];

    /// <summary>
    /// Calculate the size of a container
    /// </summary>
    public static int GetContainerSize(this Item item)
    {
        if (item is not SearchableItem container)
        {
            return 0;
        }

        var grids = container.Grids;
        var gridSize = 0;

        foreach (var grid in grids)
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
        var key = item.GetItemComponent<KeyComponent>();
        return key != null && key.Template.MaximumNumberOfUsage == 1;
    }

    /// <summary>
    /// Triggers a container to open/close.
    /// </summary>
    public static ValueTask<bool> InteractAsync(
        this BotOwner botOwner,
        WorldInteractiveObject worldInteractiveObject,
        EInteractionType action,
        CancellationToken token = default
    )
    {
        var source = ActionTaskCompletionSource.Start(token);

        if (worldInteractiveObject == null)
        {
            source.SetException(
                new ArgumentNullException($"[{botOwner.Name()}] Interacting [{action.ToString()}] with WorldInteractiveObject but is NULL")
            );
        }
        else
        {
            // NOTE: This method MUST be used for Fika compatibility
            var interactionResult = new InteractionResult(action);
            botOwner.GetPlayer.StartInteraction(worldInteractiveObject, interactionResult, source.CompleteAction);
        }

        return source.Task;
    }

    /// <summary>
    /// Calculates the amount of total and available grid slots in a container
    /// </summary>
    /// <returns>(Total Size Grid Slots, Available Grid Slots)</returns>
    public static (int total, int available) GetTotalAndAvailableGridSlots(this Grid[] grids)
    {
        if (grids is null)
        {
            return (0, 0);
        }

        var gridSize = 0;
        var containedSize = 0;

        // Loop through each grid and calculate the total and contained spaces
        foreach (var grid in grids)
        {
            gridSize += grid.GridHeight * grid.GridWidth;
            containedSize += grid.GetSizeOfContainedItems();
        }

        return (gridSize, gridSize - containedSize);
    }

    /// <summary>
    /// returns the amount of space taken up by all the items in a given grid slot
    /// </summary>
    /// <param name="grid">The grid to calculate the amount of space taken up for</param>
    /// <returns>Returns the item size as an integer</returns>
    public static int GetSizeOfContainedItems(this Grid grid)
    {
        var containedItemSize = 0;

        // Loop through each item in grid.Items (same as grid.ItemCollection.ItemsList) and accumulate the item size
        foreach (var item in grid.ItemCollection.ItemsList)
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

    /// <summary>
    /// Given an item that is stackable and can be merged,
    /// search through the inventory and find any matches of that item that are not in a secure container.
    /// </summary>
    public static Item FindItemToMerge(this InventoryController controller, Item item)
    {
        // Return null if item cannot be stacked
        if (item.StackMaxSize <= 1)
        {
            return null;
        }

        // Use the item's template id to search for the same item in the inventory
        foreach (var foundItem in controller.Inventory.GetAllItemByTemplate(item.TemplateId))
        {
            if (foundItem is null)
            {
                continue;
            }

            var rootItem = foundItem.GetRootItem();

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

    /// <summary>
    /// Returns the list of slots to loot from a corpse in priority order.
    /// When a bot already has a backpack/rig, it will attempt to loot the weapons off the bot first.
    /// Otherwise, it will loot the equipment first and loot the weapons afterward.
    /// </summary>
    public static void GetPriorityItems(
        this InventoryEquipment corpseEquipment,
        InventoryEquipment botEquipment,
        List<Item> preallocatedList
    )
    {
        // Add slots in priority order
        if (
            botEquipment.GetSlot(EquipmentSlot.Backpack).ContainedItem != null
            || botEquipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem != null
        )
        {
            GetItemInSlotsToLootNonAlloc(corpseEquipment, preallocatedList, WeaponSlots);
            GetItemInSlotsToLootNonAlloc(corpseEquipment, preallocatedList, StorageSlots);
        }
        else
        {
            GetItemInSlotsToLootNonAlloc(corpseEquipment, preallocatedList, StorageSlots);
            GetItemInSlotsToLootNonAlloc(corpseEquipment, preallocatedList, WeaponSlots);
        }

        GetItemInSlotsToLootNonAlloc(corpseEquipment, preallocatedList, OtherSlots);
    }

    private static void GetItemInSlotsToLootNonAlloc(InventoryEquipment equipment, List<Item> preallocatedList, EquipmentSlot[] slots)
    {
        foreach (var slotName in slots)
        {
            var slot = equipment.GetSlot(slotName);
            var item = slot.ContainedItem;
            if (item is null)
            {
                continue;
            }

            // Check if item is unlootable
            var unlootableComponent = item.GetItemComponent<UnlootableComponent>();
            if (
                unlootableComponent != null
                && unlootableComponent.IsUnlootableFrom(item.Parent.Container)
                && item is not Pockets // Include pockets to loot list
            )
            {
                continue;
            }

            preallocatedList.Add(item);
        }
    }

    /// <summary>
    ///  Helper to get the root item of an InteractableObject
    /// </summary>
    public static Item GetRootItem(this InteractableObject interactableObject)
    {
        return interactableObject switch
        {
            LootableContainer container => container.ItemOwner?.RootItem,
            LootItem lootItem => lootItem.ItemOwner?.RootItem,
            _ => null,
        };
    }

    /// <summary>
    ///  Helper to get the root item ID of an InteractableObject
    /// </summary>
    public static string GetRootItemId(this InteractableObject interactableObject)
    {
        return interactableObject.GetRootItem()?.Id;
    }

    /// <summary>
    ///  Helper to get the loot name of an InteractableObject, depending on the type.
    /// </summary>
    public static string GetLootName(this InteractableObject interactableObject)
    {
        return interactableObject switch
        {
            LootableContainer container => container.ItemOwner?.RootItem.Name.Localized(),
            Corpse corpse => corpse.name,
            LootItem lootItem => lootItem.ItemOwner?.RootItem.Name.Localized(),
            _ => "-",
        };
    }

    /// <summary>
    /// Check if moving an item to a slot is blocked.
    /// Except chest/rig armor.
    /// Based on <see cref="Slot.GetConflictingSlot"/>
    /// </summary>
    public static bool HasBlockingItem(this Slot slot, Item incomingItem, out Item conflictingItem)
    {
        conflictingItem = null;

        var conflictingSlots = slot.ConflictingSlots;
        if (conflictingSlots is null)
        {
            return false;
        }

        if (!incomingItem.TryGetItemComponent<SlotBlockerComponent>(out var slotBlocker))
        {
            return false;
        }

        foreach (var conflictingSlotName in slotBlocker.ConflictingSlotNames)
        {
            if (
                conflictingSlots.TryGetValue(conflictingSlotName, out var conflictingSlot)
                && conflictingSlot != slot // Exclude checking the same slot
                && conflictingSlot.ContainedItem is { } conflictItem
                && conflictItem is not Armor and not Vest // Exclude chest/rig armor
            )
            {
                conflictingItem = conflictItem;
                return true;
            }
        }

        return false;
    }

    public static Item GetFirstItem(this IEnumerable<Item> items)
    {
        switch (items)
        {
            case null:
                return null;
            case List<Item> list:
                return list.Count > 0 ? list[0] : null;
            default:
            {
                using var enumerator = items.GetEnumerator();
                return enumerator.MoveNext() ? enumerator.Current : null;
            }
        }
    }

    /// <summary>
    /// Gets all contained items (grid) of an item and its children
    /// </summary>
    /// <remarks>Does not get items in its Slots</remarks>
    public static void GetAllContainedItems(this Item item, List<Item> preAllocatedList)
    {
        if (item is not CompoundItem compoundItem)
        {
            return;
        }

        foreach (var grid in compoundItem.Grids)
        {
            foreach (var containedItem in grid.ItemCollection.ItemsList)
            {
                preAllocatedList.Add(containedItem);
                containedItem.GetAllContainedItems(preAllocatedList);
            }
        }
    }

    public static void GetAcceptableItemsInStorageSlotsNonAlloc<TItem>(
        this InventoryController inventoryController,
        IList<TItem> preAllocatedList,
        Predicate<TItem> predicate = null
    )
        where TItem : Item
    {
        inventoryController.GetAcceptableItemsNonAlloc(StorageSlots, preAllocatedList, predicate);
    }

    public static void GetPrioritizedGridsNonAlloc(this Item item, List<Grid> preAllocatedList)
    {
        switch (item)
        {
            case InventoryEquipment equipment:
                var pocketsContainers = (equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem)?.Grids ?? [];
                var vestContainers = (equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem)?.Grids ?? [];
                var backpackContainers = (equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem)?.Grids ?? [];
                preAllocatedList.AddRange(backpackContainers);
                preAllocatedList.AddRange(vestContainers);
                preAllocatedList.AddRange(pocketsContainers);
                return;
            case LootContainer container:
                preAllocatedList.AddRange(container.Grids);
                return;
            default:
                // Loose loot
                return;
        }
    }
}
