using EFT;
using EFT.InventoryLogic;

namespace LootingBots;

public static class LootingBotsExtensions
{
    public static Item GetFirstItem(this IEnumerable<Item> items)
    {
        if (items == null)
        {
            return null;
        }

        using var enumerator = items.GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current : null;
    }
}
