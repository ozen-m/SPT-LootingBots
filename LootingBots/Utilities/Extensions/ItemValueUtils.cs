using EFT.InventoryLogic;

namespace LootingBots.Utilities.Extensions;

public static class ItemValueUtils
{
    /// <summary>
    /// Calculates the sum value of its children (recursive). Excludes slots.
    /// </summary>
    public static float GetAllContainedItemsValue(this Item item, BotLog log)
    {
        var price = 0f;

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var containedItems);
        item.GetAllGridContainedItems(containedItems);
        foreach (var containedItem in containedItems)
        {
            price += LootingBots.ItemAppraiser.GetItemPrice(containedItem, log);
        }

        return price;
    }
}
