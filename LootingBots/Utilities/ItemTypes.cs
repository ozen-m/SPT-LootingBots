using EFT.InventoryLogic;

namespace LootingBots.Utilities;

public static class ItemTypes
{
    /// <summary>
    /// These are items that can be placed in <seealso cref="Inventory.FastAccessSlots"/>.
    /// </summary>
    public static bool IsPlacedInFastAccessSlots(this Item item)
    {
        return item is Magazine or Ammo or Meds or ThrowWeap;
    }

    public static bool IsNotReplaceable(this Item item)
    {
        return item.QuestItem || item.IsDogtag() || item is Weapon or Magazine or Ammo or Money;
    }

    /// <summary>
    /// Checks if a key is a Single Use Item like the "Unknown Key"
    /// </summary>
    /// <param name="item">The item to check</param>
    /// <returns>returns true if it's single use, false otherwise</returns>
    public static bool IsSingleUseKey(this Item item)
    {
        return item.TryGetItemComponent(out KeyComponent key) && key.Template.MaximumNumberOfUsage == 1;
    }
}
