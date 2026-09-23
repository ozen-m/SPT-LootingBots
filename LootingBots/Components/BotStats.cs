using System.Text;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using UnityEngine;

namespace LootingBots.Components;

public class BotStats
{
    public readonly GearValue Gear = new();

    public float NetWorth;
    public float InitialNetWorth;
    public int AvailableGridSpaces;
    public int TotalGridSpaces;

    public float Looted
    {
        get { return NetWorth - InitialNetWorth; }
    }

    public float PrimaryValue
    {
        get { return Gear.Primary.Value; }
    }

    public float SecondaryValue
    {
        get { return Gear.Secondary.Value; }
    }

    public float HolsterValue
    {
        get { return Gear.Holster.Value; }
    }

    public void AddNetValue(float itemPrice)
    {
        NetWorth += itemPrice;
    }

    public void SubtractNetValue(float itemPrice)
    {
        NetWorth -= itemPrice;
    }

    public void StatsDebugPanel(StringBuilder debugPanel)
    {
        var freeSpaceColor =
            AvailableGridSpaces <= 2 ? Color.red
            : AvailableGridSpaces < TotalGridSpaces / 2 ? Color.yellow
            : Color.green;

        debugPanel.AppendLabeledValue("Total Looted Value", $" {Looted:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Total Net Worth", $" {NetWorth:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Available Space", $" {AvailableGridSpaces} slots", Color.white, freeSpaceColor);
        debugPanel.AppendLabeledValue("Primary Value", $" {Gear.Primary.Value:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Secondary Value", $" {Gear.Secondary.Value:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Holster Value", $" {Gear.Holster.Value:n0}₽", Color.white, Color.white);
    }
}

public class GearValue
{
    public readonly ValuePair Primary = new(string.Empty, 0f);
    public readonly ValuePair Secondary = new(string.Empty, 0f);
    public readonly ValuePair Holster = new(string.Empty, 0f);

    public readonly ContainedItems Vest = new();
    public readonly ContainedItems Backpack = new();
    public readonly ContainedItems Pockets = new();

    public bool TryAddContainedItem(Item item, int size, float value)
    {
        if (ContainedItems.IsNotReplaceable(item))
        {
            return false;
        }

        return Backpack.TryAdd(item, size, value) || Vest.TryAdd(item, size, value) || Pockets.TryAdd(item, size, value);
    }
}

public class ValuePair(string id, float value)
{
    public string Id = id;
    public float Value = value;

    public void UpdatePair(string id, float value)
    {
        Id = id;
        Value = value;
    }

    public static void SwapPair(ValuePair pair1, ValuePair pair2)
    {
        (pair1.Id, pair2.Id) = (pair2.Id, pair1.Id);
        (pair1.Value, pair2.Value) = (pair2.Value, pair1.Value);
    }
}

public class ContainedItems
{
    /// <summary>
    /// A list containing all viable items for replacement with potential loot.
    /// Sorted ascending by <see cref="ContainedLootItem.ValuePerSlot"/>.
    /// </summary>
    private readonly List<ContainedLootItem> _items = [];

    /// <summary>
    /// Used for parent checks
    /// </summary>
    private Item _container;

    public ContainedLootItem this[int index]
    {
        get { return _items[index]; }
    }

    /// <summary>
    /// Find from the collection a viable item to be replaced.
    /// This assumes <see cref="_items"/> is sorted ascending by <see cref="ContainedLootItem.ValuePerSlot"/>.
    /// </summary>
    /// <param name="potentialLoot">The replacement</param>
    /// <param name="index">Index of the item to be replaced</param>
    /// <returns>True if found an item to be replaced</returns>
    public bool TryFindReplacement(ContainedLootItem potentialLoot, out int index)
    {
        for (index = 0; index < _items.Count; index++)
        {
            var replacedItem = _items[index];

            if (potentialLoot.ValuePerSlot <= replacedItem.ValuePerSlot)
            {
                break;
            }
            if (potentialLoot.Value <= replacedItem.Value)
            {
                continue;
            }
            if (potentialLoot.Size <= replacedItem.Size)
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    public bool TryAdd(Item item, int size, float value)
    {
        if (_container is null)
        {
            return false;
        }
        if (!item.IsChildOf(_container))
        {
            return false;
        }

        AddInternal(new ContainedLootItem(item, size, value));
        return true;
    }

    public void Replace(int index, ContainedLootItem loot)
    {
        _items.RemoveAt(index);
        if (IsNotReplaceable(loot.Item))
        {
            return;
        }
        AddInternal(loot);
    }

    public void OnChangeContainer(Item item)
    {
        _container = item;
        _items.Clear();

        if (item is not SearchableItem searchableItem)
        {
            return;
        }

        AddAllContainedLootItems(searchableItem);
        _items.Sort();
    }

    /// <summary>
    /// Add an item to the list, sorted by ValuePerSlot
    /// </summary>
    /// <param name="item"></param>
    private void AddInternal(ContainedLootItem item)
    {
        var index = _items.BinarySearch(item);
        if (index < 0)
        {
            index = ~index;
        }

        _items.Insert(index, item);
    }

    private void AddAllContainedLootItems(SearchableItem searchableItem)
    {
        foreach (var grid in searchableItem.Grids)
        {
            foreach (var gridItem in grid.ItemCollection.ItemsList)
            {
                switch (gridItem)
                {
                    case SearchableItem childContainer:
                        AddAllContainedLootItems(childContainer);
                        continue;
                    default:
                        if (IsNotReplaceable(gridItem))
                        {
                            continue;
                        }
                        _items.Add(new ContainedLootItem(gridItem));
                        break;
                }
            }
        }
    }

    public static bool IsNotReplaceable(Item item)
    {
        return item.QuestItem || item.IsDogtag() || item is Magazine or Ammo or Meds or Money;
    }
}

public readonly struct ContainedLootItem : IComparable<ContainedLootItem>
{
    public readonly Item Item;
    public readonly int Size;
    public readonly float Value;
    public readonly float ValuePerSlot;

    public ContainedLootItem(Item item)
    {
        Item = item;
        Value = LootingBots.ItemAppraiser.GetItemPrice(item, null);

        var cellSize = item.CalculateCellSize();
        Size = cellSize.X * cellSize.Y;
        ValuePerSlot = Value / Size;
    }

    public ContainedLootItem(Item item, int size, float value)
    {
        Item = item;
        Value = value;
        Size = size;
        ValuePerSlot = value / size;
    }

    public int CompareTo(ContainedLootItem other)
    {
        return ValuePerSlot.CompareTo(other.ValuePerSlot);
    }

    public override string ToString()
    {
        return $"LootItem: {Item.LocalizedName()}, Size: {Size}, Value: {ValuePerSlot}, ValuePerSlot: {ValuePerSlot}";
    }
}
