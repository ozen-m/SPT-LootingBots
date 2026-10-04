using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class ItemValueUtils
{
    /// <summary>
    /// Calculates the sum value of its children (recursive). Excludes slot items.
    /// </summary>
    public static float GetAllGridContainedItemsValue(this Item item, BotLog log)
    {
        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var containedItems);
        item.GetAllGridContainedItems(containedItems);

        var price = 0f;
        foreach (var containedItem in containedItems)
        {
            price += LootingBots.ItemAppraiser.GetItemPrice(containedItem, log);
        }

        return price;
    }

    /// <summary>
    /// Calculates the sum value of its children (recursive). Excludes grid items.
    /// </summary>
    public static float GetAllSlotContainedItemsValue(this Item item, BotLog log)
    {
        // Do not include weapon or armor, they're calculated differently
        if (item is Weapon || item.TryGetItemComponent(out ArmorHolderComponent _))
        {
            return 0f;
        }

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var containedItems);
        item.GetAllSlotContainedItems(containedItems);

        var price = 0f;
        foreach (var containedItem in containedItems)
        {
            price += LootingBots.ItemAppraiser.GetItemPrice(containedItem, log);
        }

        return price;
    }
}
