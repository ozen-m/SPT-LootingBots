using System.Runtime.CompilerServices;
using EFT.InventoryLogic;

namespace LootingBots.Utilities;

[Flags]
public enum EquipmentType
{
    Backpack = 1,
    TacticalRig = 2,
    ArmoredRig = 4,
    Chest = 8,
    Weapon = 16,
    Grenade = 32,
    Helmet = 64,
    Dogtag = 128,
    ArmorPlate = 256,
    Earpiece = 512,
    FaceCover = 1024,
    Eyewear = 2048,
    Armband = 4096,

    All =
        Backpack
        | TacticalRig
        | ArmoredRig
        | Chest
        | Weapon
        | Helmet
        | Grenade
        | Dogtag
        | ArmorPlate
        | Earpiece
        | FaceCover
        | Eyewear
        | Armband,
}

[Flags]
public enum CanEquipEquipmentType
{
    Backpack = EquipmentType.Backpack,
    TacticalRig = EquipmentType.TacticalRig,
    ArmoredRig = EquipmentType.ArmoredRig,
    Chest = EquipmentType.Chest,
    Weapon = EquipmentType.Weapon,
    Grenade = EquipmentType.Grenade,
    Helmet = EquipmentType.Helmet,
    Earpiece = EquipmentType.Earpiece,
    FaceCover = EquipmentType.FaceCover,
    Eyewear = EquipmentType.Eyewear,
    Armband = EquipmentType.Armband,

    All = Backpack | TacticalRig | ArmoredRig | Chest | Weapon | Helmet | Grenade | Earpiece | FaceCover | Eyewear | Armband,
}

public static class EquipmentTypeUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasBackpack(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Backpack) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasTacticalRig(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.TacticalRig) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasArmoredRig(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.ArmoredRig) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasChestArmor(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Chest) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasGrenade(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Grenade) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasWeapon(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Weapon) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasHelmet(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Helmet) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasArmorPlate(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.ArmorPlate) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasDogtag(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Dogtag) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasEarpiece(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Earpiece) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFaceCover(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.FaceCover) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasEyewear(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Eyewear) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasArmband(this EquipmentType equipmentType)
    {
        return (equipmentType & EquipmentType.Armband) != 0;
    }

    /// <summary>
    /// GClasses based off InventoryExtension.FindSlotToPickUp
    /// </summary>
    public static bool IsItemEligible(this EquipmentType allowedGear, Item item, bool toPickup = false)
    {
        return item switch
        {
            Armor => allowedGear.HasChestArmor(),
            Headwear => allowedGear.HasHelmet(),
            Backpack => allowedGear.HasBackpack(),
            Headphones => allowedGear.HasEarpiece(),
            FaceCover => allowedGear.HasFaceCover(),
            Visors => allowedGear.HasEyewear(),
            Vest vest when IsArmoredRig(vest) => allowedGear.HasArmoredRig(),
            Vest => allowedGear.HasTacticalRig(),
            ArmorPlate => allowedGear.HasArmorPlate(),
            ThrowWeap => allowedGear.HasGrenade(),
            Weapon => allowedGear.HasWeapon(),
            ArmBand => allowedGear.HasArmband(),
            BarterOther barter when barter.IsDogtag() => allowedGear.HasDogtag(),
            _ => toPickup,
        };
    }

    public static bool IsArmoredRig(Vest vest)
    {
        foreach (var slot in vest.Slots)
        {
            // If any slot is an armor slot
            if (slot is ArmorSlot)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsDogtag(this Item item)
    {
        return item is BarterOther barter && barter.Dogtag is not null;
    }

    public static bool IsDogtag(this BarterOther barter)
    {
        return barter.Dogtag is not null;
    }
}
