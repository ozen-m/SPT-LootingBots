using EFT.InventoryLogic;

namespace LootingBots.Utilities.Comparers;

public sealed class MagazineAmmoComparer : IComparer<Item>
{
    public static MagazineAmmoComparer Instance { get; } = new();

    private MagazineAmmoComparer() { }

    /// <summary>
    /// Magazine (count 2)
    /// Magazine (count 1)
    /// Ammo (stack count 2)
    /// Ammo (stack count 1)
    /// Other
    /// </summary>
    public int Compare(Item x, Item y)
    {
        if (x is Magazine xMag && y is Magazine yMag)
        {
            return yMag.Count.CompareTo(xMag.Count);
        }
        if (x is Ammo xAmmo && y is Ammo yAmmo)
        {
            return yAmmo.StackObjectsCount.CompareTo(xAmmo.StackObjectsCount);
        }
        if (x is Magazine && y is Ammo)
        {
            return -1;
        }
        if (x is Ammo && y is Magazine)
        {
            return 1;
        }
        return 0;
    }
}
