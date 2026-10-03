using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class LocationUtils
{
    /// <summary>
    /// Based on <see cref="InventoryExtension.FindGridToPickUp"/>
    /// </summary>
    public static GridItemAddress FindGridToPickUpLootNonAlloc(this Item transferTo, Item loot)
    {
        if (transferTo.Owner is not ItemController controller)
        {
            return null;
        }
        var lootParent = loot.Parent.Container.ParentItem;

        using var pooled = UnityEngine.Pool.ListPool<Grid>.Get(out var grids);
        transferTo.GetPrioritizedGridsForLootNonAlloc(loot, grids);
        foreach (var grid in grids)
        {
            if (lootParent == grid._parentItem)
            {
                // Skip grids with the same parent
                continue;
            }
            var location = grid.FindLocationForItem(loot) ?? grid.FindLocationForItemInNestedGrids(loot);
            if (location == null)
            {
                continue;
            }
            if (!ItemManipulator.DestinationCheck(loot.Parent, location, controller).Value)
            {
                continue;
            }

            return location;
        }
        return null;
    }

    /// <summary>
    /// Try to find a location in nested grids.
    /// </summary>
    /// <param name="grid">Grid to find nested searchable items' grids</param>
    /// <param name="item">Item to find location for</param>
    public static GridItemAddress FindLocationForItemInNestedGrids(this Grid grid, Item item)
    {
        foreach (var gridItem in grid._itemCollection.ItemsList)
        {
            if (gridItem is not SearchableItem searchableItem)
            {
                continue;
            }

            return searchableItem.FindGridToPickUpLootNonAlloc(item);
        }

        return null;
    }

    /// <summary>
    /// Based on <see cref="InventoryEquipmentExtension.GetPrioritizedContainersForLoot"/>.
    /// Does not include the SecuredContainer slot for InventoryEquipment.
    /// </summary>
    public static void GetPrioritizedGridsForLootNonAlloc(this Item transferTo, Item loot, List<Grid> preAllocatedList)
    {
        switch (transferTo)
        {
            case InventoryEquipment equipment:
            {
                var armbandGrids = (equipment.GetSlot(EquipmentSlot.ArmBand).ContainedItem as CompoundItem)?.Grids ?? [];
                var pocketsGrids = (equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem)?.Grids ?? [];
                var vestGrids = (equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem)?.Grids ?? [];
                var backpackGrids = (equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem)?.Grids ?? [];

                switch (loot)
                {
                    case Ammo:
                        preAllocatedList.AddRange(armbandGrids);
                        preAllocatedList.AddRange(vestGrids);
                        preAllocatedList.AddRange(pocketsGrids);
                        // preAllocatedList.AddRange(backpackGrids);
                        break;
                    case Magazine:
                        preAllocatedList.AddRange(vestGrids);
                        preAllocatedList.AddRange(armbandGrids);
                        preAllocatedList.AddRange(pocketsGrids);
                        // preAllocatedList.AddRange(backpackGrids); // Bots can't reach magazines in the backpack
                        break;
                    case ThrowWeap:
                        preAllocatedList.AddRange(pocketsGrids);
                        preAllocatedList.AddRange(armbandGrids);
                        preAllocatedList.AddRange(vestGrids);
                        // preAllocatedList.AddRange(backpackGrids);
                        break;
                    case Meds:
                        preAllocatedList.AddRange(pocketsGrids);
                        preAllocatedList.AddRange(armbandGrids);
                        preAllocatedList.AddRange(backpackGrids);
                        // preAllocatedList.AddRange(vestGrids);
                        break;
                    default:
                        preAllocatedList.AddRange(backpackGrids);
                        // preAllocatedList.AddRange(vestGrids); // Reserve vest for Magazine, Ammo, ThrowWeap
                        preAllocatedList.AddRange(armbandGrids);
                        preAllocatedList.AddRange(pocketsGrids);
                        break;
                }
                return;
            }
            case LootContainer container:
            {
                preAllocatedList.AddRange(container.Grids);
                return;
            }
            case SearchableItem searchableItem:
                preAllocatedList.AddRange(searchableItem.Grids);
                foreach (var grid in searchableItem.Grids)
                {
                    foreach (var item in grid._itemCollection.ItemsList)
                    {
                        item.GetPrioritizedGridsForLootNonAlloc(null, preAllocatedList);
                    }
                }
                return;
            default:
            {
                // Loose loot
                return;
            }
        }
    }
}
