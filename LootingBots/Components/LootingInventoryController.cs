using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Actions;
using LootingBots.Utilities;
using UnityEngine.Pool;
using EquipmentType = LootingBots.Utilities.EquipmentType;

namespace LootingBots.Components;

public class LootingInventoryController
{
    private readonly BotOwner _botOwner;
    private readonly InventoryController _botInventoryController;
    private readonly LootingBrain _lootingBrain;
    private readonly LootingTransactionController _transactionController;
    private readonly BotLog _log;
    private readonly ItemAppraiser _itemAppraiser;
    private readonly bool _isPMC;

    public readonly BotStats Stats = new();

    private readonly Action _updateActiveWeaponAction;
    private readonly Callback<IHandsController> _onWeaponTakenCallback;
    private readonly List<Action> _unsubActions = [];

    // Represents the value in roubles of the current item
    public float CurrentItemPrice;

    public bool ShouldSort = true;

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
        _transactionController = new LootingTransactionController(botOwner, _botInventoryController, _log);
        _isPMC = _botOwner.Profile.Info.Settings.Role.IsPMC();

        CalculateGearValue();
        CalculateInitialNetWorth();
        SubscribeToGearSlots();
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
            if (Stats.Gear.Primary.Id != primary.Id)
            {
                var value = _itemAppraiser.GetItemPrice(primary, _log);
                Stats.Gear.Primary.UpdatePair(primary.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.Gear.Primary.Id))
            {
                Stats.Gear.Primary.UpdatePair(string.Empty, 0f);
            }
        }

        if (secondary != null)
        {
            if (Stats.Gear.Secondary.Id != secondary.Id)
            {
                var value = _itemAppraiser.GetItemPrice(secondary, _log);
                Stats.Gear.Secondary.UpdatePair(secondary.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.Gear.Secondary.Id))
            {
                Stats.Gear.Secondary.UpdatePair(string.Empty, 0f);
            }
        }

        if (holster != null)
        {
            if (Stats.Gear.Holster.Id != holster.Id)
            {
                var value = _itemAppraiser.GetItemPrice(holster, _log);
                Stats.Gear.Holster.UpdatePair(holster.Id, value);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(Stats.Gear.Holster.Id))
            {
                Stats.Gear.Holster.UpdatePair(string.Empty, 0f);
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
            var containedItem = slot.ContainedItem;
            switch (containedItem)
            {
                case null or MobContainer:
                    continue;
                case SearchableItem searchableItem:
                {
                    // Get the price of the searchable item and its contained items
                    Stats.NetWorth += _itemAppraiser.GetItemPrice(searchableItem, _log) + GetAllContainedItemsValue(searchableItem);
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
    /// Subscribe to gear slots so when its ContainedItem is changed, grid stats is updated.
    /// </summary>
    public void SubscribeToGearSlots()
    {
        Action<Item> updateGridStatsAction = UpdateGridStats;

        var tacVestSlot = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest);
        _unsubActions.Add(tacVestSlot.ReactiveContainedItem.Subscribe(updateGridStatsAction));
        _unsubActions.Add(tacVestSlot.ReactiveContainedItem.Bind(Stats.Gear.Vest.OnChangeContainer));

        var backpackSlot = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack);
        _unsubActions.Add(backpackSlot.ReactiveContainedItem.Subscribe(updateGridStatsAction));
        _unsubActions.Add(backpackSlot.ReactiveContainedItem.Bind(Stats.Gear.Backpack.OnChangeContainer));

        var pockets = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        Stats.Gear.Pockets.OnChangeContainer(pockets);
    }

    /// <summary>
    /// Updates stats for AvailableGridSpaces and TotalGridSpaces based off the bots current gear.
    /// </summary>
    public void UpdateGridStats()
    {
        var tacVest = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
        var pockets = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        var backpack = (SearchableItem)_botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;

        var tacVestGrids = (tacVest?.Grids).GetTotalAndAvailableGridSlots();
        var pocketsGrids = (pockets?.Grids).GetTotalAndAvailableGridSlots();
        var backpackGrids = (backpack?.Grids).GetTotalAndAvailableGridSlots();

        Stats.AvailableGridSpaces = tacVestGrids.available + pocketsGrids.available + backpackGrids.available;
        Stats.TotalGridSpaces = tacVestGrids.total + pocketsGrids.total + backpackGrids.total;
    }

    private void UpdateGridStats(Item _)
    {
        UpdateGridStats();
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
        using var pooledList = ListActionPool.Get(out var lootingActions);

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
                var itemValue = itemSize > 1 ? $"{CurrentItemPrice:N0}₽ {CurrentItemPrice / itemSize:N0}₽/slot" : $"{CurrentItemPrice:N0}₽";
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
            lootingActions.Reset();
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
                        Stats.AddNetValue(action.NetWorthDelta);
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
                                    using (UnityEngine.Pool.ListPool<Item>.Get(out var modsToLoot))
                                    {
                                        await StripWeaponAsync(thrownWeapon, modsToLoot, token);
                                    }
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
                                    using (UnityEngine.Pool.ListPool<Item>.Get(out var modsToLoot))
                                    {
                                        await StripWeaponAsync(thrownWeapon, modsToLoot, token);
                                    }
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
                if (item is Weapon weapon)
                {
                    _transactionController.AddExtraAmmo(weapon);
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
                if (item is SearchableItem)
                {
                    Stats.AddNetValue(GetAllContainedItemsValue(item));
                }
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
            if (AllowedToPickup(item, itemSize))
            {
                // If we're allowed to pick up the item, but we don't have space, try to find an item to replace it with
                if (!_lootingBrain.HasFreeSpace)
                {
                    if (
                        HasReplacement(item, itemSize, out var source, out var index, out var loot)
                        && await _transactionController.ReplaceItemAsync(
                            item,
                            source[index].Item,
                            _lootingBrain.ActiveLoot.GetRootItem(),
                            token
                        )
                    )
                    {
                        var replaced = source[index];
                        Stats.AddNetValue(CurrentItemPrice - replaced.Value);
                        Stats.AvailableGridSpaces -= itemSize - replaced.Size;
                        source.Replace(index, loot);

                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug(
                                $"Replaced {replaced.Item.LocalizedName()} with {item.LocalizedName()} (Net: {CurrentItemPrice - replaced.Value:N0}₽)"
                            );
                        }
                        continue;
                    }
                }
                else if (await _transactionController.TryPickupItemAsync(item, token))
                {
                    Stats.AddNetValue(CurrentItemPrice + GetAllContainedItemsValue(item));
                    Stats.AvailableGridSpaces -= itemSize;
                    Stats.Gear.TryAddContainedItem(item, itemSize, CurrentItemPrice);
                    continue;
                }
            }

            // Strip the weapon of its mods if we cannot pick up the weapon
            if (item is Weapon weaponToStrip && LootingBots.CanStripAttachments.Value)
            {
                using (UnityEngine.Pool.ListPool<Item>.Get(out var modsToLoot))
                {
                    var successful = await StripWeaponAsync(weaponToStrip, modsToLoot, token);
                    if (!successful)
                    {
                        return false;
                    }
                }
            }
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

        switch (lootItem)
        {
            case Backpack when ShouldSwapGear(backpack, lootItem):
                GetSwapAction(lootItem, backpack, lootingActions, true);
                break;
            case Headwear when ShouldSwapGear(helmet, lootItem):
                GetSwapAction(lootItem, helmet, lootingActions, true);
                break;
            case Headphones when ShouldSwapGear(earpiece, lootItem):
                GetSwapAction(lootItem, earpiece, lootingActions, false);
                break;
            case FaceCover when ShouldSwapGear(faceCover, lootItem):
                GetSwapAction(lootItem, faceCover, lootingActions, false);
                break;
            case Visors when ShouldSwapGear(eyewear, lootItem):
                GetSwapAction(lootItem, eyewear, lootingActions, false);
                break;
            case ArmBand when ShouldSwapGear(armBand, lootItem):
                // Pack n' strap?
                GetSwapAction(lootItem, armBand, lootingActions, true);
                break;
            case Armor when ShouldSwapGear(chest, lootItem):
                GetSwapAction(lootItem, chest, lootingActions, true);
                break;
            case Vest vest:
                if (ShouldSwapGear(tacVest, lootItem))
                {
                    // If we have a chest armor equipped and the tac vest we are looting is armored,
                    // check if the armored rig is higher armor class than the chest,
                    // then make sure to drop the chest and pick up the armored rig
                    if (chest is not null && EquipmentTypeUtils.IsArmoredRig(vest))
                    {
                        if (ShouldSwapGear(chest, lootItem))
                        {
                            if (_log.DebugEnabled)
                            {
                                _log.LogDebug(
                                    $"Trying to drop chest armor [{chest.Name.Localized()}] then loot armored rig [{lootItem.Name.Localized()}]"
                                );
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
                                _log.LogDebug(
                                    $"Equipped chest armor is better than or equal to found armored rig {lootItem.Name.Localized()}"
                                );
                            }
                        }
                    }
                    else
                    {
                        GetSwapAction(lootItem, tacVest, lootingActions, true);
                    }
                }
                else if (
                    tacVest is Vest equippedVest
                    && EquipmentTypeUtils.IsArmoredRig(equippedVest)
                    && _lootingBrain.ActiveLoot.GetRootItem() is InventoryEquipment corpseEquipment
                )
                {
                    // The bot has an equipped armored rig, check if the corpse's chest has better armor
                    // If it has better armor OR same armor but larger container,
                    // drop the current armored rig then loot the armor and tac vest
                    var corpseChestArmor = corpseEquipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem;
                    var armorDifference = GetArmorDifference(corpseChestArmor, tacVest);
                    if (
                        (armorDifference > 0 || armorDifference == 0 && LootHasLargerContainer(lootItem, tacVest))
                        && AllowedToEquip(corpseChestArmor)
                    )
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug(
                                $"Trying to loot chest armor [{corpseChestArmor?.Name.Localized()}] and tac vest [{lootItem.Name.Localized()}] and drop current armored rig [{tacVest.Name.Localized()}]. Armor difference: {armorDifference}"
                            );
                        }

                        // Throw the corpse's chest armor so we can swap the vests
                        lootingActions.Add(LootingThrowAction.Rent(corpseChestArmor, 0f, false));
                        GetSwapAction(lootItem, tacVest, lootingActions, true);

                        // No need to equip the chest armor here, it will be looted next. Hopefully the bot does not get interrupted...
                    }
                }
                break;
        }

        return lootingActions.Count > 0;
    }

    /// <summary>
    /// Check if this magazine can be used by any equipped weapon
    /// </summary>
    public bool IsUsableMag(Magazine mag)
    {
        var equipment = _botInventoryController.Inventory.Equipment;
        foreach (var weaponSlot in LootUtils.WeaponSlots)
        {
            if (equipment.GetSlot(weaponSlot).ContainedItem is not Weapon weapon)
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

    /// <summary>
    /// Check if this ammo can be used by any equipped weapon
    /// </summary>
    public bool IsUsableAmmo(Ammo ammo)
    {
        var equipment = _botInventoryController.Inventory.Equipment;
        foreach (var weaponSlot in LootUtils.WeaponSlots)
        {
            if (equipment.GetSlot(weaponSlot).ContainedItem is not Weapon weapon)
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

    /// <summary>
    /// Throws all magazines from the rig that are not used by any of the weapons that the bot currently has equipped.
    /// Also records thrown mag value.
    /// </summary>
    public async Task ThrowUselessMagsAsync(Weapon thrownWeapon, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var rootItem = _lootingBrain.ActiveLoot.GetRootItem();
        var primary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem as Weapon;
        var secondary = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem as Weapon;
        var holster = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon;
        var thrownMagSlot = thrownWeapon?.GetMagazineSlot();
        var primaryMagSlot = primary?.GetMagazineSlot();
        var secondaryMagSlot = secondary?.GetMagazineSlot();
        var holsterMagSlot = holster?.GetMagazineSlot();

        using var pooledList = UnityEngine.Pool.ListPool<Magazine>.Get(out var magazines);
        _botInventoryController.GetAllGridItemsInStorageSlotsNonAlloc(magazines);

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Cleaning up old mags...");
        }

        var reservedCount = 0;
        foreach (var mag in magazines)
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

                if (!await _transactionController.TransferOrThrowItemAsync(mag, rootItem, token))
                {
                    continue;
                }

                var magPrice = _itemAppraiser.GetItemPrice(mag, _log);
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Thrown {mag.ShortName.Localized()} (-{magPrice:N0}₽)");
                }
                Stats.SubtractNetValue(magPrice);
                Stats.AvailableGridSpaces += mag.GetItemSize();
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

        var lootValue = CurrentItemPrice;

        // Loot weapon is a pistol for the holster slot
        if (lootWeapon.WeapClass.Equals("pistol"))
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
            ValuePair.SwapPair(Stats.Gear.Primary, Stats.Gear.Secondary);
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

        // If the item is a container and is bigger than what is equipped, only equip it if the armor class is the same
        if (armorDifference == 0 && LootHasLargerContainer(itemToLoot, equipped))
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
    /// Compare if <paramref name="potentialLoot"/> has a larger container than <paramref name="equipped"/>
    /// </summary>
    public bool LootHasLargerContainer(Item potentialLoot, Item equipped)
    {
        return potentialLoot.GetContainerSize() > equipped.GetContainerSize();
    }

    /// <summary>
    /// Given a piece of armor, compare it against what is current
    /// </summary>
    public bool IsBetterArmorThanEquipped(Item potentialLoot)
    {
        Item equippedArmor;
        if (potentialLoot is Headwear)
        {
            equippedArmor = CurrentHeadArmor;
        }
        else if (potentialLoot is Armor || potentialLoot is Vest vest && EquipmentTypeUtils.IsArmoredRig(vest))
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
    /// A weapon (<paramref name="potentialWeapon"/>) is defined as better when:
    ///   1. Its ammo penetration power is better than <paramref name="equippedWeapon"/>
    ///   2. Its ammo penetration power is the same AND is more valuable than <paramref name="equippedWeapon"/>
    /// </summary>
    public bool IsWeaponBetter(Weapon potentialWeapon, Weapon equippedWeapon, bool moreValuable)
    {
        if (equippedWeapon is null)
        {
            return true;
        }

        var powerDifference = GetCaliberDifference(potentialWeapon, equippedWeapon);
        if (powerDifference > 0 || powerDifference == 0 && moreValuable)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Weapon {potentialWeapon.Name.Localized()} is better versus {equippedWeapon.Name.Localized()}. Difference: {powerDifference}, IsMoreValuable: {true}"
                );
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the difference in penetration power tier.
    /// </summary>
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
            foreach (var item in magazine.Cartridges._items)
            {
                if (item is not Ammo ammo)
                {
                    continue;
                }

                var power = ammo.PenetrationPower;
                if (power > currentPower)
                {
                    currentPower = power;
                }
            }
        }

        foreach (var slot in weapon.Chambers)
        {
            if (slot.ContainedItem is not Ammo ammo)
            {
                continue;
            }

            var power = ammo.PenetrationPower;
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

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var items);

        // Slot must not be locked and is not a quest item
        foreach (var grid in parentItem.Grids)
        {
            foreach (var containedItem in grid.ItemCollection.ItemsList)
            {
                if (!containedItem.QuestItem)
                {
                    items.Add(containedItem);
                }
            }
        }
        foreach (var slot in parentItem.Slots)
        {
            if (!slot.Locked && slot.ContainedItem is not null && !slot.ContainedItem.QuestItem)
            {
                items.Add(slot.ContainedItem);
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

        var minimumValue = _isPMC ? LootingBots.PMCMinLootThreshold.Value : LootingBots.ScavMinLootThreshold.Value;

        using var pooledDictionary = DictionaryPool<Item, float>.Get(out var itemsToThrow);
        foreach (var grid in parentItem.Grids)
        {
            foreach (var childItem in grid.ItemCollection.ItemsList)
            {
                // Iterate and throw useless items for child container
                if (childItem is SearchableItem)
                {
                    await ThrowUndervaluedItemsAsync(childItem, token);
                    continue;
                }

                // Check the conditions to filter out items to keep
                if (
                    childItem.QuestItem
                    || childItem.IsDogtag()
                    || childItem is Meds or Money
                    || (childItem is Ammo ammo && IsUsableAmmo(ammo))
                )
                {
                    continue;
                }

                if (childItem is Magazine mag)
                {
                    // If it's a magazine we cannot use, throw it
                    if (!IsUsableMag(mag))
                    {
                        itemsToThrow.Add(mag, _itemAppraiser.GetItemPrice(mag, _log));
                    }
                    continue;
                }

                var value = _itemAppraiser.GetItemPrice(childItem, _log);
                if (value < minimumValue)
                {
                    itemsToThrow.Add(childItem, value);
                }
            }
        }

        if (itemsToThrow.Count > 0)
        {
            if (_log.InfoEnabled)
            {
                _log.LogInfo($"Throwing {itemsToThrow.Count} undervalued items from {parentItem.Name.Localized()}");
            }
            var rootItem = _lootingBrain.ActiveLoot.GetRootItem();

            foreach (var (toThrow, value) in itemsToThrow)
            {
                if (!await _transactionController.TransferOrThrowItemAsync(toThrow, rootItem, token))
                {
                    continue;
                }

                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Thrown {toThrow.Name.Localized()} (-{value:N0}₽)");
                }
                Stats.SubtractNetValue(value);
                Stats.AvailableGridSpaces += toThrow.GetItemSize();
                _lootingBrain.IgnoreLoot(toThrow.Id);
            }

            return;
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug($"No undervalued items found to throw in {parentItem.Name.Localized()}");
        }
    }

    /// <summary>
    /// Strip and loot a weapon's attachments.
    /// </summary>
    public Task<bool> StripWeaponAsync(Weapon weapon, List<Item> itemsToAdd, CancellationToken token = default)
    {
        foreach (var mod in weapon.Mods)
        {
            // Check if the mod's slot is not required, can be modded in raid, and is not a magazine
            if (mod.Parent.Container is Slot { Required: false } && mod is { RaidModdable: true } and not Magazine)
            {
                itemsToAdd.Add(mod);
            }
        }

        if (itemsToAdd.Count > 0)
        {
            if (_log.InfoEnabled)
            {
                _log.LogInfo($"Trying to strip attachments of weapon: {weapon.Name.Localized()}");
            }

            // Call TryAddItemsToBot with the filtered items
            return TryAddItemsToBotAsync(itemsToAdd, token);
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug($"No attachments to strip for weapon: {weapon.Name.Localized()}");
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Check if the item being looted meets the loot value threshold specified in the mod settings.
    /// PMC bots use the PMC loot threshold, all other bots such as scavs, bosses, and raiders will use the scav threshold.
    /// </summary>
    public bool IsValuableEnough(float itemPrice)
    {
        // If the bot is a PMC, compare the price against the PMC loot threshold. For all other bot types use the scav threshold
        var min = (_isPMC ? LootingBots.PMCMinLootThreshold : LootingBots.ScavMinLootThreshold).Value;
        var max = (_isPMC ? LootingBots.PMCMaxLootThreshold : LootingBots.ScavMaxLootThreshold).Value;

        // If max is set to 0, do not check against max threshold
        return itemPrice >= min && (max == 0f || itemPrice <= max);
    }

    /// <summary>
    /// Check if the item being looted is allowed to be equipped by the bot as specified in the mod settings.
    /// PMC bots use the PMC allowed gear to equip config, all other bots such as scavs, bosses, and raiders will use the scav equip config.
    /// </summary>
    public bool AllowedToEquip(Item lootItem)
    {
        return _isPMC
            ? ((EquipmentType)LootingBots.PMCGearToEquip.Value).IsItemEligible(lootItem)
            : ((EquipmentType)LootingBots.ScavGearToEquip.Value).IsItemEligible(lootItem);
    }

    /// <summary>
    /// Check if the item being looted is allowed to be picked up by the bot as specified in the mod settings.
    /// PMC bots use the PMC allowed items to pick up config,
    /// all other bots such as scavs, bosses, and raiders will use the scav allowed items to pick up config.
    /// </summary>
    public bool AllowedToPickup(Item lootItem, int itemSize = 1)
    {
        var pickupNotRestricted = _isPMC
            ? LootingBots.PMCGearToPickup.Value.IsItemEligible(lootItem, true)
            : LootingBots.ScavGearToPickup.Value.IsItemEligible(lootItem, true);

        // All usable mags and money should be considered eligible to loot. Otherwise, all other items fall subject to the mod settings for restricting pickup and loot value thresholds
        return lootItem is Money
            || lootItem is Magazine mag && IsUsableMag(mag)
            || lootItem is Ammo ammo && IsUsableAmmo(ammo)
            || (
                pickupNotRestricted
                && (
                    lootItem.IsDogtag() || IsValuableEnough(CurrentItemPrice / itemSize) // Divide by slots to get price per slot
                )
            );
    }

    /// <summary>
    /// Try to find an item to be replaced for the potential item.
    /// </summary>
    /// <param name="source">The item's "parent". From the backpack, vest, or pockets.</param>
    /// <param name="index">Index of the item to be replaced from <paramref name="source"/>.</param>
    /// <returns>True if found an item to be replaced.</returns>
    public bool HasReplacement(Item lootItem, int itemSize, out ContainedItems source, out int index, out ContainedLootItem loot)
    {
        loot = new ContainedLootItem(lootItem, itemSize, CurrentItemPrice);
        if (Stats.Gear.Backpack.TryFindReplacement(loot, out index))
        {
            source = Stats.Gear.Backpack;
            return true;
        }
        if (Stats.Gear.Vest.TryFindReplacement(loot, out index))
        {
            source = Stats.Gear.Vest;
            return true;
        }
        if (Stats.Gear.Pockets.TryFindReplacement(loot, out index))
        {
            source = Stats.Gear.Pockets;
            return true;
        }

        source = null;
        index = -1;
        return false;
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

        // Include contained items in calculating NetWorthDelta
        toEquipValue += GetAllContainedItemsValue(toEquip);
        toSwapValue += GetAllContainedItemsValue(toSwap);

        var swapAction = LootingSwapAction.Rent(toEquip, toSwap, toEquipValue - toSwapValue, transferItems);
        lootingActions.Add(swapAction);
    }

    public void SetRootItemOwner(IItemOwner owner)
    {
        _transactionController.SetRootItemOwner(owner);
    }

    public void Unsubscribe()
    {
        foreach (var action in _unsubActions)
        {
            action();
        }
    }

    /// <summary>
    /// Calculates the sum value of its children (recursive). Excludes slots.
    /// </summary>
    private float GetAllContainedItemsValue(Item item)
    {
        var price = 0f;

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var containedItems);
        item.GetAllGridContainedItems(containedItems);
        foreach (var containedItem in containedItems)
        {
            price += _itemAppraiser.GetItemPrice(containedItem, _log);
        }

        return price;
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
                if (_log.WarningEnabled)
                {
                    _log.LogWarning("Unable to UpdateActiveWeapon");
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
