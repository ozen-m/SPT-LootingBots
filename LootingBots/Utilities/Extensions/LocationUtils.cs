using EFT;
using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class LocationUtils
{
    /// <summary>
    /// The localized name of an address' parent item.
    /// </summary>
    public static string LocalizedParentName(this ItemAddress parent)
    {
        return parent.Container.ParentItem.LocalizedName();
    }

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
            var location = grid.FindLocationForItem(loot);
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
    /// Based on <see cref="InventoryEquipmentExtension.GetPrioritizedContainersForLoot"/>.
    /// Does not include the SecuredContainer slot for InventoryEquipment.
    /// </summary>
    public static void GetPrioritizedGridsForLootNonAlloc(this Item transferTo, Item loot, List<Grid> preAllocatedList)
    {
        switch (transferTo)
        {
            case InventoryEquipment equipment:
            {
                var armband = equipment.GetSlot(EquipmentSlot.ArmBand).ContainedItem;
                var backpack = equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
                var pocketsGrids = (equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem)?.Grids ?? [];
                var vestGrids = (equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem)?.Grids ?? [];

                switch (loot)
                {
                    case Ammo:
                        armband.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        preAllocatedList.AddRange(vestGrids);
                        preAllocatedList.AddRange(pocketsGrids);
                        // backpack.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        break;
                    case Magazine:
                        preAllocatedList.AddRange(vestGrids);
                        armband.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        preAllocatedList.AddRange(pocketsGrids);
                        // backpack.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        break;
                    case ThrowWeap:
                        preAllocatedList.AddRange(pocketsGrids);
                        armband.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        preAllocatedList.AddRange(vestGrids);
                        // backpack.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        break;
                    case Meds:
                        preAllocatedList.AddRange(pocketsGrids);
                        armband.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        backpack.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        // preAllocatedList.AddRange(vestGrids);
                        break;
                    default:
                        backpack.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        // preAllocatedList.AddRange(vestGrids); // Reserve vest for Magazine, Ammo, ThrowWeap
                        armband.GetPrioritizedGridsForLootNonAlloc(loot, preAllocatedList);
                        preAllocatedList.AddRange(pocketsGrids);
                        break;
                }
                return;
            }
            case SearchableItem searchableItem:
                // Prioritize nested grids
                foreach (var grid in searchableItem.Grids)
                {
                    foreach (var item in grid._itemCollection.ItemsList)
                    {
                        if (item is SearchableItem)
                        {
                            item.GetPrioritizedGridsForLootNonAlloc(null, preAllocatedList);
                        }
                    }
                }
                preAllocatedList.AddRange(searchableItem.Grids);
                return;
            default:
                // Loose loot
                return;
        }
    }
}
