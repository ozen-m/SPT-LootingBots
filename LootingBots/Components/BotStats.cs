using System.Text;
using Comfort.Common;
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

    public readonly Deferred<float> TotalWeight;
    public float WarningLimit = 10000f;
    public float OverweightLimit = 10000f;

    private readonly Player _player;

    public BotStats(Player player)
    {
        _player = player;
        if (LootingBots.UseWeightRestriction.Value)
        {
            TotalWeight = new Deferred<float>(UpdateWeight);
            UpdateWeightLimits();
        }
    }

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

        if (LootingBots.UseWeightRestriction.Value)
        {
            var weightColor =
                TotalWeight > OverweightLimit ? Color.red
                : TotalWeight > WarningLimit ? Color.yellow
                : Color.green;
            debugPanel.AppendLabeledValue("Weight", $" {TotalWeight.Value:N1}kg", Color.white, weightColor);
        }
    }

    /// <summary>
    /// Limits should be updated when: strength skill levels up, new/removed health.Effect of type IEndurance,
    /// but is unlikely to happen to a bot so only initialize on spawn.
    /// </summary>
    /// <seealso cref="EFT.UI.Health.HealthParametersPanel.Show"/>
    private void UpdateWeightLimits()
    {
        var health = _player.HealthController;
        var relativeModifier = _player.Skills.CarryingWeightRelativeModifier * health.CarryingWeightRelativeModifier;
        var absoluteModifier = health.CarryingWeightAbsoluteModifier;
        var staminaConfig = Singleton<GlobalConfiguration>.Instance.Stamina;
        var lowerOverweightLimit = Mathf.Min(staminaConfig.WalkOverweightLimits.x, staminaConfig.BaseOverweightLimits.x);
        lowerOverweightLimit = Mathf.Min(lowerOverweightLimit, staminaConfig.SprintOverweightLimits.x);
        lowerOverweightLimit = Mathf.Min(lowerOverweightLimit, staminaConfig.WalkSpeedOverweightLimits.x);

        WarningLimit = lowerOverweightLimit * relativeModifier + absoluteModifier;
        OverweightLimit = staminaConfig.UpperOverweightLimit * relativeModifier + absoluteModifier;
    }

    /// <summary>
    /// Get the total weight of all items in all slots. Excludes the SecuredContainer.
    /// </summary>
    /// <remarks>
    /// Cannot use Inventory.TotalWeight since SAIN overrides it, and base EFT includes the SecuredContainer.
    /// <code>
    /// TotalWeight = player.skills.StrengthBuffElite
    ///     ? player.Inventory.TotalWeightEliteSkill
    ///     : player.Inventory.TotalWeight;
    /// </code>
    /// </remarks>
    private float UpdateWeight()
    {
        var equipment = _player.Inventory.Equipment;

        var weight = 0f;
        foreach (var slotName in LootUtils.AllSlots)
        {
            weight += equipment.GetSlot(slotName).ContainedItem?.TotalWeight ?? 0f;
        }

        return weight;
    }
}

public class GearValue
{
    public readonly ValuePair Primary = new(string.Empty, 0f);
    public readonly ValuePair Secondary = new(string.Empty, 0f);
    public readonly ValuePair Holster = new(string.Empty, 0f);

    public readonly ContainedItems Backpack = new();
    public readonly ContainedItems Vest = new();
    public readonly ContainedItems Pockets = new();

    public bool TryAddContainedItem(Item item, int size, float value)
    {
        if (item.IsNotReplaceable())
        {
            return false;
        }

        return Backpack.TryAdd(item, size, value) || Vest.TryAdd(item, size, value) || Pockets.TryAdd(item, size, value);
    }

    public bool TryRemoveContainedItem(Item item)
    {
        if (item.IsNotReplaceable())
        {
            return false;
        }

        if (item is not SearchableItem searchableItem)
        {
            return Backpack.TryRemove(item) || Vest.TryRemove(item) || Pockets.TryRemove(item);
        }

        // Technically unreachable since we don't throw SearchableItems
        var removedSome = false;
        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var gridItems);
        searchableItem.GetAllGridContainedItems(gridItems);
        foreach (var gridItem in gridItems)
        {
            var wasRemoved = Backpack.TryRemove(gridItem) || Vest.TryRemove(gridItem) || Pockets.TryRemove(gridItem);
            if (!removedSome)
            {
                removedSome = wasRemoved;
            }
        }
        return removedSome;
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
            if (potentialLoot.Size > replacedItem.Size)
            {
                continue;
            }
            if (potentialLoot.Item.CurrentAddress == null)
            {
                // TODO: Check if still needed
                if (LootingBots.LootLog.WarningEnabled)
                {
                    LootingBots.LootLog.LogWarning(
                        $"Removing invalid contained item: has no valid parent, discarded? [{potentialLoot.Item.ToFullString()}]"
                    );
                }
                _items.RemoveAt(index);
                index--;
                continue;
            }
            return true;
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

        if (item is SearchableItem container)
        {
            AddAllContainedLootItems(container);
            return true;
        }
        AddInternal(new ContainedLootItem(item, size, value));
        return true;
    }

    public bool TryRemove(Item item)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Item == item)
            {
                _items.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public void Replace(int index, ContainedLootItem loot)
    {
        _items.RemoveAt(index);
        if (loot.Item.IsNotReplaceable())
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
    }

    /// <summary>
    /// Add an item to the list, sorted by ValuePerSlot
    /// </summary>
    /// <param name="item"></param>
    private void AddInternal(ContainedLootItem item)
    {
        if (_items.Contains(item))
        {
            return;
        }

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
                        if (gridItem.IsNotReplaceable())
                        {
                            continue;
                        }
                        AddInternal(new ContainedLootItem(gridItem));
                        break;
                }
            }
        }
    }
}

public readonly struct ContainedLootItem : IComparable<ContainedLootItem>, IEquatable<ContainedLootItem>
{
    public readonly Item Item;
    public readonly int Size;
    public readonly float Value;
    public readonly float ValuePerSlot;

    public ContainedLootItem(Item item)
    {
        Item = item;
        Value = LootingBots.ItemAppraiser.GetItemPrice(item, null);
        Size = item.GetItemSize();
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

    public bool Equals(ContainedLootItem other)
    {
        return Item == other.Item;
    }

    public override bool Equals(object obj)
    {
        return obj is ContainedLootItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Item.GetHashCode();
    }

    public override string ToString()
    {
        return $"LootItem: {Item.LocalizedName()}, Size: {Size}, Value: {Value}, ValuePerSlot: {ValuePerSlot}";
    }

    public static bool operator ==(ContainedLootItem left, ContainedLootItem right)
    {
        return left.Item == right.Item;
    }

    public static bool operator !=(ContainedLootItem left, ContainedLootItem right)
    {
        return left.Item != right.Item;
    }
}
