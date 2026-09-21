using System.Text;
using EFT;
using UnityEngine;

namespace LootingBots.Components;

public class BotStats
{
    public readonly GearValue WeaponValues = new();

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
        get { return WeaponValues.Primary.Value; }
    }

    public float SecondaryValue
    {
        get { return WeaponValues.Secondary.Value; }
    }

    public float HolsterValue
    {
        get { return WeaponValues.Holster.Value; }
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
        debugPanel.AppendLabeledValue("Primary Value", $" {WeaponValues.Primary.Value:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Secondary Value", $" {WeaponValues.Secondary.Value:n0}₽", Color.white, Color.white);
        debugPanel.AppendLabeledValue("Holster Value", $" {WeaponValues.Holster.Value:n0}₽", Color.white, Color.white);
    }
}

public class GearValue
{
    public readonly ValuePair Primary = new(string.Empty, 0f);
    public readonly ValuePair Secondary = new(string.Empty, 0f);
    public readonly ValuePair Holster = new(string.Empty, 0f);
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
