using System.Text;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Actions;
using LootingBots.Utilities;
using UnityEngine;
using UnityEngine.Pool;
using EquipmentType = LootingBots.Utilities.EquipmentType;

namespace LootingBots.Components;

public class GearValue
{
    public readonly ValuePair Primary = new(string.Empty, 0f);
    public readonly ValuePair Secondary = new(string.Empty, 0f);
    public readonly ValuePair Holster = new(string.Empty, 0f);
}

public class ValuePair(string _id, float _value)
{
    public string Id = _id;
    public float Value = _value;

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

public class BotStats
{
    public readonly GearValue WeaponValues = new();

    public float NetWorth;
    public float InitialNetWorth;
    public int AvailableGridSpaces;
    public int TotalGridSpaces;

    public float Looted => NetWorth - InitialNetWorth;
    public float PrimaryValue => WeaponValues.Primary.Value;
    public float SecondaryValue => WeaponValues.Secondary.Value;
    public float HolsterValue => WeaponValues.Holster.Value;

    public void AddNetValue(float itemPrice)
    {
        NetWorth += itemPrice;
    }

    public void SubtractNetValue(float itemPrice)
    {
        NetWorth -= itemPrice;
    }

    public void ApplyNetValueDelta(float itemPrice)
    {
        NetWorth += itemPrice;
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

public class LootingInventoryController
{
    private readonly BotLog _log;
    private readonly LootingTransactionController _transactionController;
    private readonly BotOwner _botOwner;
    private readonly InventoryController _botInventoryController;
    private readonly LootingBrain _lootingBrain;
    private readonly ItemAppraiser _itemAppraiser;
    private readonly Action _updateActiveWeaponAction;
    private readonly Callback<IHandsController> _onWeaponTakenCallback;

    public readonly BotStats Stats = new();

    public Item CurrentArmorVest
    {
        get { return _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem; }
    }

    public Item CurrentArmorRig
    {
        get
        {
            var tacVest = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
            return tacVest?.GetItemComponent<ArmorHolderComponent>()?.Item;
        }
    }

    public Item CurrentHeadArmor
    {
        get
        {
            var helmet = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem;
            return helmet?.GetItemComponent<ArmorHolderComponent>()?.Item;
        }
    }

    public Item CurrentTorsoArmor
    {
        get { return CurrentArmorRig ?? CurrentArmorVest; }
    }

    // Represents the value in roubles of the current item
    public float CurrentItemPrice;

    public bool ShouldSort = true;

    public LootingInventoryController(BotOwner botOwner, LootingBrain lootingBrain)
    {
        _log = new BotLog(LootingBots.LootLog, botOwner);

        _lootingBrain = lootingBrain;
        _itemAppraiser = LootingBots.ItemAppraiser;
        _updateActiveWeaponAction = UpdateActiveWeapon;
        _onWeaponTakenCallback = OnWeaponTaken;

        // Initialize bot inventory controller
        _botInventoryController = botOwner.GetPlayer.InventoryController;
        _botOwner = botOwner;
        _transactionController = new LootingTransactionController(_botInventoryController, _log);

        CalculateGearValue();
        CalculateInitialNetWorth();
        UpdateGridStats();
    }

    /// <summary>
    /// Calculates the value of the bot's current weapons to use in weapon swap comparison checks
    /// </summary>
    public void CalculateGearValue()
    {
        if (_log.DebugEnabled)
        {
            _log.LogDebug("Calculating gear value...");
        }

        var primary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        var secondary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
        var holster = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

        if (primary != null)
        {
            if (Stats.WeaponValues.Primary.Id != primary.Id)
            {
                var value = _itemAppraiser.GetItemPrice(primary, _log);
                Stats.WeaponValues.Primary.UpdatePair(primary.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.WeaponValues.Primary.Id))
            {
                Stats.WeaponValues.Primary.UpdatePair(string.Empty, 0f);
            }
        }

        if (secondary != null)
        {
            if (Stats.WeaponValues.Secondary.Id != secondary.Id)
            {
                var value = _itemAppraiser.GetItemPrice(secondary, _log);
                Stats.WeaponValues.Secondary.UpdatePair(secondary.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.WeaponValues.Secondary.Id))
            {
                Stats.WeaponValues.Secondary.UpdatePair(string.Empty, 0f);
            }
        }

        if (holster != null)
        {
            if (Stats.WeaponValues.Holster.Id != holster.Id)
            {
                var value = _itemAppraiser.GetItemPrice(holster, _log);
                Stats.WeaponValues.Holster.UpdatePair(holster.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.WeaponValues.Holster.Id))
            {
                Stats.WeaponValues.Holster.UpdatePair(string.Empty, 0f);
            }
        }
    }

    public void CalculateInitialNetWorth()
    {
        if (_log.DebugEnabled)
        {
            _log.LogDebug("Calculating initial net worth...");
        }

        Stats.NetWorth = 0f;
        foreach (var slot in _botInventoryController.Inventory.Equipment._cachedSlots)
        {
            if (string.Equals(slot.Name, "securedcontainer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var containedItem = slot.ContainedItem;
            switch (containedItem)
            {
                case null:
                    continue;
                case SearchableItem searchableItem:
                {
                    // Get the price of the searchable item itself
                    Stats.NetWorth += _itemAppraiser.GetItemPrice(searchableItem, _log);

                    // Get the prices of items in its grids
                    // This assumes no further items with containers
                    foreach (var grid in searchableItem.Grids)
                    {
                        foreach (var item in grid.ItemCollection.ItemsList)
                        {
                            Stats.NetWorth += _itemAppraiser.GetItemPrice(item, _log);
                        }
                    }
                    continue;
                }
                default:
                    Stats.NetWorth += _itemAppraiser.GetItemPrice(containedItem, _log);
                    continue;
            }
        }
        Stats.InitialNetWorth = Stats.NetWorth;
    }

    /// <summary>
    /// Updates stats for AvailableGridSpaces and TotalGridSpaces based off the bots current gear.
    /// </summary>
    public void UpdateGridStats()
    {
        var tacVest = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
        var backpack = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
        var pockets = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;

        var freePockets = LootUtils.GetAvailableGridSlots(pockets?.Grids);
        var freeTacVest = LootUtils.GetAvailableGridSlots(tacVest?.Grids);
        var freeBackpack = LootUtils.GetAvailableGridSlots(backpack?.Grids);

        Stats.AvailableGridSpaces = freeBackpack + freePockets + freeTacVest;
        Stats.TotalGridSpaces = (tacVest?.Grids?.Length ?? 0) + (backpack?.Grids?.Length ?? 0) + (pockets?.Grids?.Length ?? 0);
    }

    /// <summary>
    /// Sorts the items in the tactical vest so that items prefer to be in slots that match their size.
    /// i.e a 1x1 item will be placed in a 1x1 slot instead of a 1x2 slot
    /// </summary>
    public async Task SortCompoundItemAsync(CompoundItem compoundItem)
    {
        ShouldSort = false;

        if (compoundItem != null)
        {
            var result = ItemManipulator.Sort(compoundItem, _botInventoryController, true);
            if (result.Failed)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"Failed to execute {nameof(SortCompoundItemAsync)}. Error: {result.Error}");
                }
                return;
            }

            var networkResult = await _transactionController.TryRunNetworkTransactionWithTimeoutAsync(result);
            if (networkResult.Failed)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"Failed to execute {nameof(SortCompoundItemAsync)}. Network Error: {networkResult.Error}");
                }
            }
        }
    }

    /// <summary>
    /// Main driving method which kicks off the logic for what a bot will do with the loot found.
    /// If bots are looting something that is equippable, and they have nothing equipped in that slot, they will always equip it.
    /// If the bot decides not to equip the item then it will attempt to put in an available container slot.
    /// </summary>
    public async Task<bool> TryAddItemsToBotAsync(List<Item> items, CancellationToken token = default)
    {
        var lootingActions = ListActionPool.Rent();
        try
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(item.Name))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug("Item is NULL");
                    }
                    continue;
                }

                if (LootingBots.UseExamineTime.Value)
                {
                    await SimulateExamineTimeAsync(item, token);
                }

                // Item info, such as: name, size, price
                var itemName = item.Name.Localized();
                var itemSize = item.GetItemSize();
                CurrentItemPrice = _itemAppraiser.GetItemPrice(item, _log);

                if (_log.DebugEnabled)
                {
                    var itemValue =
                        itemSize > 1 ? $"{CurrentItemPrice:N0}₽ {CurrentItemPrice / itemSize:N0}₽/slot" : $"{CurrentItemPrice:N0}₽";
                    _log.LogDebug($"Loot found: {itemName} ({itemValue})");
                }

                // Ignore magazines that a bot cannot actively use
                if (item is Magazine mag && !IsUsableMag(mag))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Cannot use mag: {itemName}. Skipping");
                    }

                    continue;
                }

                // Check to see if we need to swap gear
                ListActionPool.Reset(lootingActions);
                var canEquipGear = GetEquipAction(item, lootingActions);
                if (canEquipGear)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Found equip action for: {itemName}");
                    }

                    foreach (var action in lootingActions)
                    {
                        var actionResult = await action.ExecuteAsync(_transactionController, token);
                        if (actionResult)
                        {
                            Stats.ApplyNetValueDelta(action.NetWorthDelta);
                        }
                        else
                        {
                            // Break the chain if the action fails
                            break;
                        }

                        // Do post actions
                        if (action is LootingSwapAction swapAction)
                        {
                            if (swapAction.TransferItems)
                            {
                                if (swapAction.ToSwap is Weapon thrownWeapon)
                                {
                                    // If we swapped away our previous weapon, throw away its mags and strip the attachments
                                    await ThrowUselessMagsAsync(thrownWeapon, token);
                                    if (LootingBots.CanStripAttachments.Value)
                                    {
                                        await StripWeaponAsync(thrownWeapon, token);
                                    }
                                }
                                else
                                {
                                    // To make space we throw undervalued items in our newly equipped item
                                    // Then loot the thrown item
                                    await ThrowUndervaluedItemsAsync(swapAction.Item, token);
                                    await LootNestedItemsAsync(swapAction.ToSwap, token);
                                }
                            }
                        }
                        else if (action is LootingThrowAction throwAction)
                        {
                            if (throwAction.TransferItems)
                            {
                                var thrownItem = throwAction.Item;

                                // Ignore thrown loot
                                _lootingBrain.IgnoreLoot(thrownItem.Id);

                                if (thrownItem is Weapon thrownWeapon)
                                {
                                    // Throw mags of thrown weapon and strip attachments
                                    await ThrowUselessMagsAsync(thrownWeapon, token);
                                    if (LootingBots.CanStripAttachments.Value)
                                    {
                                        await StripWeaponAsync(thrownWeapon, token);
                                    }
                                }
                                else
                                {
                                    // Loot thrown item's children
                                    await LootNestedItemsAsync(thrownItem, token);
                                }
                            }
                        }
                    }

                    // Do post-equip actions
                    // We looted a weapon, calculate gear value
                    if (item is Weapon)
                    {
                        CalculateGearValue();
                    }

                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Finished equip action for: {itemName}");
                    }

                    continue;
                }

                // Check to see if we can equip the item
                if (AllowedToEquip(item) && await _transactionController.TryEquipItemAsync(item, token))
                {
                    Stats.AddNetValue(CurrentItemPrice);
                    continue;
                }

                // Try to pick up any nested items before trying to pick up the item.
                // This helps when looting rigs to transfer ammo to the bots active rig
                if (item is SearchableItem searchableItem)
                {
                    var success = await LootNestedItemsAsync(searchableItem, token);

                    if (!success)
                    {
                        return false;
                    }
                }

                // Check to see if we can pick up the item
                if (AllowedToPickup(item, itemSize) && await _transactionController.TryPickupItemAsync(item, token))
                {
                    Stats.AddNetValue(CurrentItemPrice);
                    UpdateGridStats();
                }
                else if (item is Weapon weapon && LootingBots.CanStripAttachments.Value)
                {
                    // Strip the weapon of its mods if we cannot pick up the weapon
                    var successful = await StripWeaponAsync(weapon, token);
                    if (!successful)
                    {
                        return false;
                    }
                }
            }
        }
        finally
        {
            ListActionPool.Return(lootingActions);
        }

        return true;
    }

    /// <summary>
    /// Use the ExamineTime of an object and the AttentionExamineValue of the bot to calculate the delay for discovering an item while looting.
    /// Taken from ExamineOperationClass constructor
    /// </summary>
    public Task SimulateExamineTimeAsync(Item item, CancellationToken token = default)
    {
        return LootingTransactionController.SimulatePlayerDelayAsync(
            item.ExamineTime * 1000f / (1f + _botOwner.Profile.Skills.AttentionExamineValue),
            token
        );
    }

    /// <summary>
    /// Updates the bot's known weapon list and tells the bot to switch to the best available weapon
    /// </summary>
    public void UpdateActiveWeapon()
    {
        if (_botOwner == null)
        {
            return;
        }

        var weaponSelector = _botOwner.WeaponManager?.Selector;
        if (weaponSelector is null)
        {
            return;
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Updating weapons");
        }
        weaponSelector.UpdateWeaponsList();
        weaponSelector.IsWeaponReady = false;
        weaponSelector.SetSlotItem(_onWeaponTakenCallback, true);
    }

    /// <summary>
    /// Method to refill magazines with ammo and also reload the current weapon with a new magazine
    /// </summary>
    private void RefillAndReload()
    {
        _botOwner.WeaponManager.Reload?.TryFillMagazines();
        _botOwner.WeaponManager.Reload?.TryReload();
    }

    /// <summary>
    /// Checks certain slots to see if the item we are looting is "better" than what is currently equipped.
    /// View <see cref="ShouldSwapGear"/> for criteria.
    /// Gear is checked in a specific order so that bots will try to swap gear that is a "container" first
    /// like backpacks and tac vests. This is to make sure they aren't putting loot in an item they will ultimately decide to drop.
    /// </summary>
    /// <returns>True if equip actions were found</returns>
    public bool GetEquipAction(Item lootItem, List<LootingAction> lootingActions)
    {
        if (!AllowedToEquip(lootItem))
        {
            return false;
        }

        // Bosses cannot swap gear as many bosses have custom logic tailored to their loadouts
        if (lootItem is Weapon lootWeapon && !BotTypeUtils.IsBoss(_botOwner.Profile.Info.Settings.Role))
        {
            GetWeaponEquipAction(lootWeapon, lootingActions);
            return lootingActions.Count > 0;
        }

        var helmet = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem;
        var earpiece = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem;
        var faceCover = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FaceCover).ContainedItem;
        var eyewear = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Eyewear).ContainedItem;
        var chest = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem;
        var armBand = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmBand).ContainedItem;
        var tacVest = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
        var backpack = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;

        if (EquipmentTypeUtils.IsBackpack(lootItem) && ShouldSwapGear(backpack, lootItem))
        {
            GetSwapAction(lootItem, backpack, lootingActions, true);
        }
        else if (EquipmentTypeUtils.IsHelmet(lootItem) && ShouldSwapGear(helmet, lootItem))
        {
            GetSwapAction(lootItem, helmet, lootingActions, true);
        }
        else if (EquipmentTypeUtils.IsEarpiece(lootItem) && ShouldSwapGear(earpiece, lootItem))
        {
            GetSwapAction(lootItem, earpiece, lootingActions, false);
        }
        else if (EquipmentTypeUtils.IsFaceCover(lootItem) && ShouldSwapGear(faceCover, lootItem))
        {
            GetSwapAction(lootItem, faceCover, lootingActions, false);
        }
        else if (EquipmentTypeUtils.IsEyewear(lootItem) && ShouldSwapGear(eyewear, lootItem))
        {
            GetSwapAction(lootItem, eyewear, lootingActions, false);
        }
        else if (EquipmentTypeUtils.IsArmband(lootItem) && ShouldSwapGear(armBand, lootItem))
        {
            // Pack n' strap?
            GetSwapAction(lootItem, armBand, lootingActions, true);
        }
        else if (EquipmentTypeUtils.IsChestArmor(lootItem) && ShouldSwapGear(chest, lootItem))
        {
            // TODO: Add check for chest armor vs equipped armored rig? Cannot guarantee a new tac vest :/
            GetSwapAction(lootItem, chest, lootingActions, true);
        }
        else if (EquipmentTypeUtils.IsTacticalRig(lootItem) && ShouldSwapGear(tacVest, lootItem))
        {
            // If we have a chest armor equipped and the tac vest we are looting is armored,
            // check if the armored rig is higher armor class than the chest,
            // then make sure to drop the chest and pick up the armored rig
            if (chest is not null && EquipmentTypeUtils.IsArmoredRig(lootItem))
            {
                if (ShouldSwapGear(chest, lootItem))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug("Trying to drop chest armor then loot armored rig");
                    }

                    var chestValue = _itemAppraiser.GetItemPrice(chest, _log);
                    var throwAction = LootingThrowAction.Rent(chest, -chestValue);
                    lootingActions.Add(throwAction);
                    GetSwapAction(lootItem, tacVest, lootingActions, true);
                }
                else
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Equipped chest armor is better than or equal to found armored rig {lootItem.Name.Localized()}");
                    }
                }
            }
            else
            {
                GetSwapAction(lootItem, tacVest, lootingActions, true);
            }
        }

        return lootingActions.Count > 0;
    }

    public bool IsUsableMag(Magazine mag)
    {
        return mag != null && HasAcceptableMagazineSlot(_botInventoryController.Inventory.Equipment, mag);
    }

    public bool IsUsableAmmo(Ammo ammo)
    {
        return ammo != null && HasAcceptableAmmoSlot(_botInventoryController.Inventory.Equipment, ammo);
    }

    private static readonly EquipmentSlot[] _weaponSlots =
    [
        EquipmentSlot.FirstPrimaryWeapon,
        EquipmentSlot.SecondPrimaryWeapon,
        EquipmentSlot.Holster,
    ];

    private static bool HasAcceptableMagazineSlot(InventoryEquipment equipment, Magazine mag)
    {
        foreach (var weaponSlot in _weaponSlots)
        {
            var slot = equipment.GetSlot(weaponSlot);
            if (slot?.ContainedItem is not Weapon weapon)
            {
                continue;
            }

            var magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot != null && magazineSlot.CanAccept(mag))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAcceptableAmmoSlot(InventoryEquipment equipment, Ammo ammo)
    {
        foreach (var weaponSlot in _weaponSlots)
        {
            var slot = equipment.GetSlot(weaponSlot);
            if (slot?.ContainedItem is not Weapon weapon)
            {
                continue;
            }

            foreach (var chamber in weapon.Chambers)
            {
                if (chamber.CanAccept(ammo))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly List<Magazine> _throwUselessMagsScratch = [];

    /// <summary>
    /// Throws all magazines from the rig that are not used by any of the weapons that the bot currently has equipped.
    /// Also records thrown mag value.
    /// </summary>
    public async Task ThrowUselessMagsAsync(Weapon thrownWeapon, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var primary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem as Weapon;
        var secondary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem as Weapon;
        var holster = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon;
        var thrownMagSlot = thrownWeapon?.GetMagazineSlot();
        var primaryMagSlot = primary?.GetMagazineSlot();
        var secondaryMagSlot = secondary?.GetMagazineSlot();
        var holsterMagSlot = holster?.GetMagazineSlot();

        _throwUselessMagsScratch.Clear();
        _botInventoryController.GetReachableItemsOfTypeNonAlloc(_throwUselessMagsScratch);

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Cleaning up old mags...");
        }

        var reservedCount = 0;
        foreach (var mag in _throwUselessMagsScratch)
        {
            var fitsInThrown = thrownMagSlot?.CanAccept(mag) == true;
            var fitsInPrimary = primaryMagSlot?.CanAccept(mag) == true;
            var fitsInSecondary = secondaryMagSlot?.CanAccept(mag) == true;
            var fitsInHolster = holsterMagSlot?.CanAccept(mag) == true;

            var fitsInEquipped = fitsInPrimary || fitsInSecondary || fitsInHolster;
            var isSharedMag = fitsInThrown && fitsInEquipped;
            if (isSharedMag && reservedCount < 2)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Reserving shared mag {mag.Name.Localized()}");
                }

                reservedCount++;
            }
            else if (!fitsInEquipped || reservedCount >= 2)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Removing useless mag {mag.Name.Localized()}");
                }

                await LootingTransactionController.SimulatePlayerDelayAsync(token: token);

                if (!await _transactionController.ThrowItemAsync(mag, token))
                {
                    continue;
                }

                var magPrice = _itemAppraiser.GetItemPrice(mag, _log);
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Thrown {mag.ShortName.Localized()} (-{magPrice:N0}₽)");
                }
                Stats.SubtractNetValue(magPrice);
                _lootingBrain.IgnoreLoot(mag.Id);
            }
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Cleaning up old mags...done");
        }
    }

    /// <summary>
    /// Determines the kind of equip action the bot should take when encountering a weapon.
    /// Bots will always prefer to replace weapons that have lower value when encountering a higher value weapon.
    /// </summary>
    public void GetWeaponEquipAction(Weapon lootWeapon, List<LootingAction> lootingActions)
    {
        var primary = (Weapon)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        var secondary = (Weapon)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
        var holster = (Weapon)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

        var isPistol = lootWeapon.WeapClass.Equals("pistol");
        var lootValue = CurrentItemPrice;

        if (isPistol)
        {
            if (holster is null)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to holster");
                }

                var moveAction = LootingMoveAction.Rent(lootWeapon, null, lootValue);
                lootingActions.Add(moveAction);
            }
            else
            {
                var holsterValue = Stats.HolsterValue;
                if (IsWeaponBetter(lootWeapon, holster, lootValue > holsterValue))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug(
                            $"Trying to swap {lootWeapon.Name.Localized()} (₽{lootValue}) with {holster.Name.Localized()} (₽{holsterValue}) in holster"
                        );
                    }

                    var swapAction = LootingSwapAction.Rent(lootWeapon, holster, lootValue - holsterValue, true);
                    lootingActions.Add(swapAction);
                }
            }

            return;
        }

        // If we have no primary, equip the weapon to primary
        // Then swap if it's not better than secondary
        if (primary is null)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to primary slot");
            }

            var moveAction = LootingMoveAction.Rent(lootWeapon, null, lootValue);
            lootingActions.Add(moveAction);

            if (secondary != null && IsWeaponBetter(secondary, lootWeapon, Stats.SecondaryValue > lootValue))
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"then swapping it to the secondary slot [Occupied by: {secondary.Name.Localized()}]");
                }

                var swapAction = LootingSwapAction.Rent(secondary, lootWeapon, 0f, false);
                lootingActions.Add(swapAction);
            }

            return;
        }

        // If there is no secondary, equip the weapon to secondary
        // Then swap if it's better than primary
        if (secondary is null)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to secondary slot");
            }

            var moveAction = LootingMoveAction.Rent(lootWeapon, null, lootValue);
            lootingActions.Add(moveAction);

            if (IsWeaponBetter(lootWeapon, primary, lootValue > Stats.PrimaryValue))
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"then swapping it to the primary slot [Occupied by: {primary.Name.Localized()}]");
                }

                var swapAction = LootingSwapAction.Rent(lootWeapon, primary, 0f, false);
                lootingActions.Add(swapAction);
            }

            return;
        }

        // Both primary and secondary slots are equipped
        // Put the better weapon in the primary slot
        if (IsWeaponBetter(secondary, primary, Stats.SecondaryValue > Stats.PrimaryValue))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Trying to swap the secondary {secondary.Name.Localized()} with the primary ({primary.Name.Localized()}) because it is a better weapon"
                );
            }

            var swapAction = LootingSwapAction.Rent(secondary, primary, 0f, false);
            lootingActions.Add(swapAction);

            // Update the variables since we swapped the two
            (primary, secondary) = (secondary, primary);
            ValuePair.SwapPair(Stats.WeaponValues.Primary, Stats.WeaponValues.Secondary);
        }

        // If the weapon is better than the secondary
        // swap it with the secondary (effectively throwing the secondary),
        // then move the new weapon to the primary slot if the new weapon is better than the primary weapon
        var thrownSecondary = false;
        if (IsWeaponBetter(lootWeapon, secondary, lootValue > Stats.SecondaryValue))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Trying to swap {lootWeapon.Name.Localized()} (₽{lootValue}) with secondary {secondary.Name.Localized()} (₽{Stats.SecondaryValue})"
                );
            }

            var equipAction = LootingSwapAction.Rent(lootWeapon, secondary, lootValue - Stats.SecondaryValue, true);
            lootingActions.Add(equipAction);
            thrownSecondary = true;
        }
        if (IsWeaponBetter(lootWeapon, primary, lootValue > Stats.PrimaryValue))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    thrownSecondary
                        ? $"then swapping it to the primary slot [Occupied by: {primary.Name.Localized()}]"
                        : $"Trying to swap {lootWeapon.Name.Localized()} (₽{lootValue}) with primary {primary.Name.Localized()} (₽{Stats.PrimaryValue})"
                );
            }

            // If we didn't throw the secondary, calculate net worth delta and strip the swapped out primary weapon
            var swapAction = LootingSwapAction.Rent(
                lootWeapon,
                primary,
                thrownSecondary ? 0f : lootValue - Stats.PrimaryValue,
                !thrownSecondary
            );
            lootingActions.Add(swapAction);
        }
    }

    /// <summary>
    /// Checks to see if the bot should swap its currently equipped gear with the item to loot.<br/>
    /// Bot will swap under the following criteria:<br/>
    /// 1. The item has an armor rating, and it's higher than what is currently equipped.<br/>
    ///
    /// 2. The item is a container, and it's larger than what is equipped.<br/>
    /// - Will not switch out if the item we are looting is lower armor class than what is equipped<br/>
    ///
    /// 3. The item is more valuable<br/>
    /// - Will not switch out if the item we are looting is lower armor class than what is equipped<br/>
    /// </summary>
    public bool ShouldSwapGear(Item equipped, Item itemToLoot)
    {
        if (equipped is null)
        {
            return false;
        }

        // Bosses cannot swap gear as many bosses have custom logic tailored to their loadouts
        if (BotTypeUtils.IsBoss(_botOwner.Profile.Info.Settings.Role))
        {
            return false;
        }

        if (equipped.Parent.Container is Slot equippedSlot && equippedSlot.HasBlockingItem(itemToLoot, out var conflictingItem))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Cannot swap {itemToLoot.Name.Localized()} with {equipped.Name.Localized()} because of conflicting item {conflictingItem.Name.Localized()}"
                );
            }
            return false;
        }

        // Equip if we found item with a better armor class
        var armorDifference = GetArmorDifference(itemToLoot, equipped);
        if (armorDifference > 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Found better armor {itemToLoot.Name.Localized()} versus {equipped.Name.Localized()}. Difference: {armorDifference}"
                );
            }
            return true;
        }

        var foundBiggerContainer = false;

        // If the item is a container, calculate the size and see if it's bigger than what is equipped
        if (equipped.IsContainer)
        {
            var equippedSize = (equipped as SearchableItem).GetContainerSize();
            var itemToLootSize = (itemToLoot as SearchableItem).GetContainerSize();

            foundBiggerContainer = itemToLootSize > equippedSize;
        }

        // If the item is bigger than what is equipped, only equip it if the armor class is the same
        if (armorDifference == 0 && foundBiggerContainer)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Found bigger container {itemToLoot.Name.Localized()} versus {equipped.Name.Localized()}");
            }
            return true;
        }

        // If the item is more valuable than what is equipped, only equip it if the armor class is the same
        if (armorDifference == 0 && LootIsMoreValuable(equipped))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Found more valuable gear {itemToLoot.Name.Localized()} versus {equipped.Name.Localized()}");
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Given a piece of armor, compare it against what is current
    /// </summary>
    public bool IsBetterArmorThanEquipped(Item potentialLoot)
    {
        Item equippedArmor;
        if (EquipmentTypeUtils.IsHelmet(potentialLoot))
        {
            equippedArmor = CurrentHeadArmor;
        }
        else if (EquipmentTypeUtils.IsChestArmor(potentialLoot) || EquipmentTypeUtils.IsArmoredRig(potentialLoot))
        {
            equippedArmor = CurrentTorsoArmor;
        }
        else
        {
            // Potential loot is not armor
            return false;
        }
        return GetArmorDifference(potentialLoot, equippedArmor) > 0;
    }

    /// <summary>
    /// Compare current item value (Item to loot price) with equipped value
    /// </summary>
    private bool LootIsMoreValuable(Item equippedItem)
    {
        return CurrentItemPrice > LootingBots.ItemAppraiser.GetItemPrice(equippedItem, _log);
    }

    /// <summary>
    /// Calculate the difference between the armor classes of the item to loot and the currently equipped item
    /// </summary>
    /// <returns>Returns a positive integer if the item to loot has a higher armor class than what is currently equipped</returns>
    public static int GetArmorDifference(Item itemToLoot, Item equippedItem)
    {
        return GetArmorClass(itemToLoot) - GetArmorClass(equippedItem);
    }

    /// <summary>
    /// Gets the max armor class of an item
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public static int GetArmorClass(Item item)
    {
        // Get item's armor class then get armor class of plates inside armor slots
        var currentArmorClass = item?.GetItemComponent<ArmorComponent>()?.ArmorClass ?? 0;

        if (item is not CompoundItem compoundItem)
        {
            return currentArmorClass;
        }

        foreach (var slot in compoundItem.Slots)
        {
            if (slot.ContainedItem is not ArmoredEquipment armoredEquipment)
            {
                // Slot is not containing an armor-plate/built-in-insert
                continue;
            }

            var armorComponent = armoredEquipment.Armor;
            if (armorComponent is null)
            {
                continue;
            }

            var armorClass = armorComponent.ArmorClass;
            if (armorClass > currentArmorClass)
            {
                currentArmorClass = armorClass;
            }
        }

        return currentArmorClass;
    }

    /// <summary>
    /// A weapon (potentialWeapon) is defined as better when:
    ///   1. Its ammo is in the same tier or is better than the equippedWeapon
    ///   2. It's more valuable than the equippedWeapon
    /// TODO: Solely base on ammo penetration power?
    /// </summary>
    public bool IsWeaponBetter(Weapon potentialWeapon, Weapon equippedWeapon, bool moreValuable)
    {
        if (equippedWeapon is null)
        {
            return true;
        }

        var powerDifference = GetCaliberDifference(potentialWeapon, equippedWeapon);
        if (powerDifference >= 0 && moreValuable)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Weapon {potentialWeapon.Name.Localized()} is better versus {equippedWeapon?.Name.Localized()}. Difference: {powerDifference}, IsMoreValuable: {true}"
                );
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the difference in penetration power tier.
    /// </summary>
    /// <param name="potentialWeapon"></param>
    /// <param name="equippedWeapon"></param>
    /// <returns></returns>
    public int GetCaliberDifference(Weapon potentialWeapon, Weapon equippedWeapon)
    {
        return GetWeaponPenetrationPower(potentialWeapon) / 10 - GetWeaponPenetrationPower(equippedWeapon) / 10;
    }

    /// <summary>
    /// Gives a weapon's max penetration power from its chamber and magazines.
    /// </summary>
    public int GetWeaponPenetrationPower(Weapon weapon)
    {
        if (weapon is null)
        {
            return 0;
        }

        var currentPower = 0;
        var magazine = weapon.GetCurrentMagazine();
        if (magazine != null)
        {
            foreach (var ammo in magazine.Cartridges._items)
            {
                var power = ((AmmoTemplate)ammo.Template).PenetrationPower;
                if (power > currentPower)
                {
                    currentPower = power;
                }
            }
        }

        foreach (var shell in weapon.ShellsInChambers)
        {
            if (shell is null)
            {
                continue;
            }

            var power = shell.PenetrationPower;
            if (power > currentPower)
            {
                currentPower = power;
            }
        }
        return currentPower;
    }

    /// <summary>
    /// Searches throughout the children of a compound item and attempts to loot them
    /// </summary>
    public async Task<bool> LootNestedItemsAsync(Item item, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // Do not limit to SearchableItem
        // So we can loot slots of thrown/swapped out helmets, etc., they can be valuable
        if (item is not CompoundItem parentItem)
        {
            return true;
        }

        var items = UnityEngine.Pool.ListPool<Item>.Get();
        try
        {
            foreach (var nestedItem in parentItem.GetFirstLevelItems())
            {
                // Check the conditions to filter out items
                var isItemLocked = nestedItem.CurrentAddress?.Container is Slot slot && slot.Locked;

                if (nestedItem.Id != parentItem.Id && !nestedItem.QuestItem && !isItemLocked)
                {
                    items.Add(nestedItem);
                }
            }

            if (items.Count > 0)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Looting {items.Count} items from {parentItem.Name.Localized()}");
                }

                await LootingTransactionController.SimulatePlayerDelayAsync(LootingBrain.LootingStartDelay, token);
                return await TryAddItemsToBotAsync(items, token);
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No nested items found to loot in {parentItem.Name.Localized()}");
            }

            return true;
        }
        finally
        {
            UnityEngine.Pool.ListPool<Item>.Release(items);
        }
    }

    /// <summary>
    /// Searches through the child items of a container and attempts to throw them
    /// </summary>
    /// <param name="item">Only throws items of a container of type <see cref="SearchableItem"/></param>
    public async Task ThrowUndervaluedItemsAsync(Item item, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // Limit to only SearchableItem
        // As opposed to LootNestedItems, we only need to throw away its children if it's a container
        if (item is not SearchableItem parentItem)
        {
            return;
        }

        var itemsToThrow = DictionaryPool<Item, float>.Get();
        try
        {
            var botType = _botOwner.Profile.Info.Settings.Role;
            var isPmc = botType.IsPMC();

            foreach (var nestedItem in parentItem.GetFirstLevelItems())
            {
                // Check the conditions to filter out items
                if (
                    nestedItem.Id == parentItem.Id
                    || nestedItem.QuestItem
                    || (nestedItem.CurrentAddress?.Container is Slot slot && slot.Locked) // Slot is locked
                    || (nestedItem is Magazine mag && IsUsableMag(mag)) // Mag can be used
                    || (nestedItem is Ammo ammo && IsUsableAmmo(ammo)) // Ammo can be used
                    || nestedItem is Meds // Do not throw med items
                    || nestedItem is BarterOther // Do not throw dog tags
                )
                {
                    continue;
                }

                var value = _itemAppraiser.GetItemPrice(nestedItem, _log);
                var minimumValue = isPmc ? LootingBots.PMCMinLootThreshold.Value : LootingBots.ScavMinLootThreshold.Value;
                var isUnderValued = value < minimumValue;
                if (!isUnderValued)
                {
                    continue;
                }

                itemsToThrow.Add(nestedItem, value);
            }

            if (itemsToThrow.Count > 0)
            {
                if (_log.InfoEnabled)
                {
                    _log.LogInfo($"Throwing {itemsToThrow.Count} undervalued items from {parentItem.Name.Localized()}");
                }

                foreach (var (toThrow, value) in itemsToThrow)
                {
                    await LootingTransactionController.SimulatePlayerDelayAsync(token: token);

                    if (!await _transactionController.ThrowItemAsync(toThrow, token))
                    {
                        continue;
                    }

                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Thrown {toThrow.Name.Localized()} (-{value:N0}₽)");
                    }
                    Stats.SubtractNetValue(value);
                    _lootingBrain.IgnoreLoot(toThrow.Id);
                }

                return;
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No undervalued items found to throw in {parentItem.Name.Localized()}");
            }
        }
        finally
        {
            DictionaryPool<Item, float>.Release(itemsToThrow);
        }
    }

    /// <summary>
    /// Strip and loot a weapon's attachments.
    /// </summary>
    public async Task<bool> StripWeaponAsync(Weapon weapon, CancellationToken token = default)
    {
        var itemsToAdd = UnityEngine.Pool.ListPool<Item>.Get();
        try
        {
            foreach (var weaponSlot in weapon.Slots)
            {
                if (weaponSlot.Required)
                {
                    continue;
                }

                foreach (var weaponMod in weaponSlot.Items)
                {
                    // check if the weaponMod is an actual mod and if it can be modded in raid
                    if (weaponMod is Mod mod && mod.RaidModdable)
                    {
                        itemsToAdd.Add(weaponMod);
                    }
                }
            }

            if (itemsToAdd.Count > 0)
            {
                if (_log.InfoEnabled)
                {
                    _log.LogInfo($"Trying to strip attachments of weapon: {weapon.Name.Localized()}");
                }

                // Call TryAddItemsToBot with the filtered items
                var success = await TryAddItemsToBotAsync(itemsToAdd, token);
                if (!success)
                {
                    return false;
                }
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No attachments to strip for weapon: {weapon.Name.Localized()}");
            }

            return true;
        }
        finally
        {
            UnityEngine.Pool.ListPool<Item>.Release(itemsToAdd);
        }
    }

    /// <summary>
    /// Check if the item being looted meets the loot value threshold specified in the mod settings.
    /// PMC bots use the PMC loot threshold, all other bots such as scavs, bosses, and raiders will use the scav threshold.
    /// </summary>
    public bool IsValuableEnough(float itemPrice)
    {
        var botType = _botOwner.Profile.Info.Settings.Role;
        var isPmc = botType.IsPMC();

        // If the bot is a PMC, compare the price against the PMC loot threshold. For all other bot types use the scav threshold
        var min = (isPmc ? LootingBots.PMCMinLootThreshold : LootingBots.ScavMinLootThreshold).Value;
        var max = (isPmc ? LootingBots.PMCMaxLootThreshold : LootingBots.ScavMaxLootThreshold).Value;

        // If max is set to 0, do not check against max threshold
        return itemPrice >= min && (max == 0f || itemPrice <= max);
    }

    /// <summary>
    /// Check if the item being looted is allowed to be equipped by the bot as specified in the mod settings.
    /// PMC bots use the PMC allowed gear to equip config, all other bots such as scavs, bosses, and raiders will use the scav equip config.
    /// </summary>
    public bool AllowedToEquip(Item lootItem)
    {
        var eligiblePmcGear = (EquipmentType)LootingBots.PMCGearToEquip.Value;
        var eligibleScavGear = (EquipmentType)LootingBots.ScavGearToEquip.Value;

        var botType = _botOwner.Profile.Info.Settings.Role;
        var isPmc = botType.IsPMC();
        var allowedToEquip = isPmc ? eligiblePmcGear.IsItemEligible(lootItem) : eligibleScavGear.IsItemEligible(lootItem);

        return allowedToEquip;
    }

    /// <summary>
    /// Check if the item being looted is allowed to be picked up by the bot as specified in the mod settings.
    /// PMC bots use the PMC allowed items to pick up config,
    /// all other bots such as scavs, bosses, and raiders will use the scav allowed items to pick up config.
    /// </summary>
    public bool AllowedToPickup(Item lootItem, int itemSize = 1)
    {
        var botType = _botOwner.Profile.Info.Settings.Role;
        var isPmc = botType.IsPMC();
        var pickupNotRestricted = isPmc
            ? LootingBots.PMCGearToPickup.Value.IsItemEligible(lootItem, true)
            : LootingBots.ScavGearToPickup.Value.IsItemEligible(lootItem, true);

        // All usable mags and money should be considered eligible to loot. Otherwise, all other items fall subject to the mod settings for restricting pickup and loot value thresholds
        return IsUsableMag(lootItem as Magazine)
            || IsUsableAmmo(lootItem as Ammo)
            || lootItem is Money
            || (
                pickupNotRestricted
                && (
                    EquipmentTypeUtils.IsDogtag(lootItem) || IsValuableEnough(CurrentItemPrice / itemSize) // Divide by slots to get price per slot
                )
            );
    }

    /// <summary>
    /// Generates a SwapAction to be executed by the transaction controller.
    /// </summary>
    public void GetSwapAction(Item toEquip, Item toSwap, List<LootingAction> lootingActions, bool transferItems = false)
    {
        var toEquipValue = CurrentItemPrice;
        var toSwapValue = _itemAppraiser.GetItemPrice(toSwap, _log);
        if (_log.DebugEnabled)
        {
            _log.LogDebug(
                $"Trying to equip {toEquip.Name.Localized()} (₽{toEquipValue:N0}) and swap with {toSwap.Name.Localized()} (₽{toSwapValue:N0}){(transferItems ? $" then loot {toSwap.Name.Localized()}" : string.Empty)}"
            );
        }

        var swapAction = LootingSwapAction.Rent(toEquip, toSwap, toEquipValue - toSwapValue, transferItems);
        lootingActions.Add(swapAction);
    }

    public void SetRootItemOwner(IItemOwner owner)
    {
        _transactionController.SetRootItemOwner(owner);
    }

    /// <summary>
    /// Based on <see cref="BotWeaponSelector.OnWeaponTaken"/>
    /// </summary>
    private void OnWeaponTaken(Result<IHandsController> hands)
    {
        var weaponSelector = _botOwner.WeaponManager.Selector;
        weaponSelector.IsChanging = false;
        var allFine = false;

        if (hands.Succeed)
        {
            _botOwner.WeaponManager.UpdateHandsController(hands.Value, out allFine);
        }

        if (_botOwner.BotState != EBotState.Active)
        {
            if (_botOwner.BotState == EBotState.PreActive)
            {
                return;
            }
        }
        else
        {
            if (allFine)
            {
                // Update LastEquippedSlot and WeaponManager.CurrentWeaponInfo
                var currentEquippedSlot = weaponSelector._mainWeapon; // MainWeapon is set by WeaponSelector.SetSlotItem(Callback<IHandsController> onSpawn, bool order)
                weaponSelector._lastEquipmentSlot = currentEquippedSlot;
                weaponSelector.OnActiveEquipmentSlotChanged?.Invoke(currentEquippedSlot);
                RefillAndReload();

                weaponSelector._errorCounter = 0;
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Current weapon is: {hands.Value.Item}");
                }
                return;
            }
            if (++weaponSelector._errorCounter >= 20)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogWarning("Unable to Selector.TakeMainWeapon");
                }
                return;
            }
        }

        // Not active, not preactive, not allFine, not reached max errors, hands.failed
        if (_botOwner.GetPlayer.HandsController != null)
        {
            _botOwner.GetPlayer.HandsController.FastForwardCurrentState();
        }
        _botOwner.AITaskManager.RegisterDelayedTask(_botOwner, 0.5f, _updateActiveWeaponAction);
    }
}
