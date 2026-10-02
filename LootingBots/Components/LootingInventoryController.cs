using Comfort.Common;
using Diz.LanguageExtensions;
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

        _ = OnSpawnAsync();
    }

    public async Task OnSpawnAsync(CancellationToken token = default)
    {
        try
        {
            var equipment = _botInventoryController.Inventory.Equipment;
            await TransferItemsToBackpackAsync(equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem);
            await TransferItemsToBackpackAsync(equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem);
            await TransferItemsToBackpackAsync(equipment.GetSlot(EquipmentSlot.ArmBand).ContainedItem);

            var backpack = equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
            using (DictionaryPool<Item, float>.Get(out var itemsToThrow))
            {
                GetUndervaluedItems(backpack, itemsToThrow);
                if (itemsToThrow.Count > 0)
                {
                    await ThrowUndervaluedItemsAsync(backpack, itemsToThrow, null, null, token);
                }
            }

            CalculateGearValue();
            CalculateInitialNetWorth();
            SubscribeToGearSlots();
            UpdateGridStats();
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            LootingBots.LootLog.LogError(ex.ToString());
        }
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

        var equipment = _botInventoryController.Inventory.Equipment;
        var primary = equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        var secondary = equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
        var holster = equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

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
        Stats.NetWorth += Stats.PrimaryValue;
        Stats.NetWorth += Stats.SecondaryValue;
        Stats.NetWorth += Stats.HolsterValue;
        foreach (var slot in _botInventoryController.Inventory.Equipment._cachedSlots)
        {
            var containedItem = slot.ContainedItem;
            switch (containedItem)
            {
                case null or MobContainer or Weapon:
                    continue;
                case SearchableItem searchableItem:
                {
                    // Get the price of the searchable item and its contained items
                    Stats.NetWorth += _itemAppraiser.GetItemPrice(searchableItem, _log) + searchableItem.GetAllContainedItemsValue(_log);
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

        var equipment = _botInventoryController.Inventory.Equipment;

        var tacVestSlot = equipment.GetSlot(EquipmentSlot.TacticalVest);
        _unsubActions.Add(tacVestSlot.ReactiveContainedItem.Subscribe(updateGridStatsAction));
        _unsubActions.Add(tacVestSlot.ReactiveContainedItem.Bind(Stats.Gear.Vest.OnChangeContainer));

        var backpackSlot = equipment.GetSlot(EquipmentSlot.Backpack);
        _unsubActions.Add(backpackSlot.ReactiveContainedItem.Subscribe(updateGridStatsAction));
        _unsubActions.Add(backpackSlot.ReactiveContainedItem.Bind(Stats.Gear.Backpack.OnChangeContainer));

        var pockets = equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        Stats.Gear.Pockets.OnChangeContainer(pockets);
    }

    /// <summary>
    /// Updates stats for AvailableGridSpaces and TotalGridSpaces based off the bots current gear.
    /// </summary>
    public void UpdateGridStats()
    {
        var equipment = _botInventoryController.Inventory.Equipment;
        var tacVest = (SearchableItem)equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
        var pockets = (SearchableItem)equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        var backpack = (SearchableItem)equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;

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

    public ValueTask TransferItemsToBackpackAsync(Item source)
    {
        if (source is not SearchableItem)
        {
            return new ValueTask();
        }
        var backpack = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
        if (backpack is null)
        {
            return new ValueTask();
        }

        using var pooledItems = UnityEngine.Pool.ListPool<Item>.Get(out var items);
        using var pooledMove = UnityEngine.Pool.ListPool<MoveResult>.Get(out var moveResults);

        source.GetAllGridContainedItems(items);
        foreach (var item in items)
        {
            // Keep these items
            if (item.IsPlacedInFastAccessSlots())
            {
                continue;
            }
            var location = backpack.FindGridToPickUpLootNonAlloc(item);
            if (location is null)
            {
                continue;
            }
            var moveResult = ItemManipulator.Move(item, location, _botInventoryController, true);
            if (moveResult.Failed)
            {
                continue;
            }

            moveResults.Add(moveResult.Value);
        }

        if (moveResults.Count == 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No items to transfer from {source.LocalizedName()} to the backpack");
            }
            return new ValueTask();
        }

        var moveOperationsResults = new MoveMultipleResult(moveResults, _transactionController, 0f);
        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Transferring {moveOperationsResults.Count} items from {source.LocalizedName()} to the backpack");
        }
        return new ValueTask(moveOperationsResults.ExecuteAsync());
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
    public async Task<bool> TryAddItemsToBotAsync(List<Item> items, bool tryToEquip = true, CancellationToken token = default)
    {
        using var pooledList = ListActionPool.Get(out var lootingActions);

        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();

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

            // Ignore magazines or ammo that a bot cannot actively use
            if ((item is Magazine mag && !IsUsableMag(mag)) || (item is Ammo ammo && !IsUsableAmmo(ammo)))
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Cannot use mag/ammo: {itemName}. Skipping");
                }
                continue;
            }

            // Check to see if we need to swap gear
            lootingActions.Reset();
            var canEquipGear = tryToEquip && GetEquipAction(item, lootingActions);
            if (canEquipGear)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Found equip action for: {itemName}");
                }

                foreach (var action in lootingActions)
                {
                    var actionResult = await action.ExecuteAsync(_transactionController, token);
                    if (!actionResult)
                    {
                        // Break the chain if the action fails
                        break;
                    }

                    Stats.AddNetValue(action.NetWorthDelta);
                    await action.PostActionsAsync(this, token);
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

            // Check to see if we can pick up the item
            if (AllowedToPickup(item, itemSize))
            {
                // Check to see if this is an item that we can merge with another item in the inventory
                if (await _transactionController.TryMergeItemAsync(item, token))
                {
                    continue;
                }

                // Try nest the container if we're allowed
                // TODO: Add weight check
                if (item is SearchableItem searchableItem && LootingBots.AllowContainerNesting.Value)
                {
                    if (await TryNestContainerAsync(searchableItem, itemSize, token))
                    {
                        continue;
                    }
                }
                // If we're allowed to pick up the item, but we don't have space, try to find an item to replace it with
                else if (!_lootingBrain.HasFreeSpace)
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
                    Stats.AddNetValue(CurrentItemPrice);
                    Stats.AvailableGridSpaces -= itemSize;
                    Stats.Gear.TryAddContainedItem(item, itemSize, CurrentItemPrice);

                    if (item is SearchableItem pickedUpContainer)
                    {
                        Stats.AddNetValue(pickedUpContainer.GetAllContainedItemsValue(_log));
                        var (total, available) = pickedUpContainer.Grids.GetTotalAndAvailableGridSlots();
                        Stats.TotalGridSpaces += total;
                        Stats.AvailableGridSpaces += available; // TODO: Double check
                    }

                    continue;
                }
            }

            // We cannot equip or pick up this item,
            // so strip its mods if it's a weapon, or loot nested items if it's a compound item.
            if (item is Weapon weaponToStrip)
            {
                if (!LootingBots.CanStripAttachments.Value)
                {
                    continue;
                }

                using (UnityEngine.Pool.ListPool<Item>.Get(out var modsToLoot))
                {
                    if (!await StripWeaponAsync(weaponToStrip, modsToLoot, token))
                    {
                        return false;
                    }
                }
            }
            else if (!await LootNestedItemsAsync(item, token))
            {
                return false;
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
            using var pooledObject = UnityEngine.Pool.ListPool<Item>.Get(out var usableMagazines);
            if (IsAbleToEquip(lootWeapon, _lootingBrain.ActiveLoot.GetRootItem(), usableMagazines))
            {
                GetWeaponEquipAction(lootWeapon, lootingActions);

                if (lootingActions.Count > 0 && usableMagazines.Count > 0)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Trying to loot {usableMagazines.Count} magazines for {lootWeapon.LocalizedName()}");
                    }
                    lootingActions.Add(LootingLootAction.Rent(usableMagazines, this));
                }
            }
            else if (_log.DebugEnabled)
            {
                _log.LogDebug($"Unable to equip {lootWeapon.LocalizedName()}, cannot find any magazines for it");
            }

            return lootingActions.Count > 0;
        }

        EquipmentSlot? gearToReplace = null;
        var toTransfer = true;
        switch (lootItem)
        {
            case Backpack:
                gearToReplace = EquipmentSlot.Backpack;
                break;
            case Headwear:
                gearToReplace = EquipmentSlot.Headwear;
                break;
            case Headphones:
                gearToReplace = EquipmentSlot.Earpiece;
                toTransfer = false;
                break;
            case FaceCover:
                gearToReplace = EquipmentSlot.FaceCover;
                toTransfer = false;
                break;
            case Visors:
                gearToReplace = EquipmentSlot.Eyewear;
                toTransfer = false;
                break;
            case ArmBand:
                gearToReplace = EquipmentSlot.ArmBand;
                break;
            case Armor:
                gearToReplace = EquipmentSlot.ArmorVest;
                break;
            case Vest:
                gearToReplace = EquipmentSlot.TacticalVest;
                break;
            case SearchableItem:
                gearToReplace = EquipmentSlot.ArmBand; // Pack 'n' strap
                break;
        }

        if (gearToReplace is not null)
        {
            GetGearAction(lootItem, gearToReplace.Value, lootingActions, toTransfer);
        }

        return lootingActions.Count > 0;
    }

    /// <summary>
    /// Check if this magazine can be used by any equipped weapon.
    /// </summary>
    public bool IsUsableMag(Magazine mag)
    {
        if (mag.FirstRealAmmo() is not Ammo ammoInMag)
        {
            return false;
        }

        var equipment = _botInventoryController.Inventory.Equipment;
        foreach (var weaponSlot in LootUtils.WeaponSlots)
        {
            if (equipment.GetSlot(weaponSlot).ContainedItem is not Weapon weapon)
            {
                continue;
            }
            if (IsUsableAmmoForWeapon(weapon, ammoInMag) && weapon.GetMagazineSlot() is { } slot && slot.CanAccept(mag))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if this magazine can be used by <paramref name="weapon"/>.
    /// We need to check if the ammo inside the magazine is compatible with the weapon
    /// since some magazines can support multiple calibers.
    /// </summary>
    public bool IsUsableMagForWeapon(Weapon weapon, Magazine mag)
    {
        return mag.FirstRealAmmo() is Ammo ammoInMag
            && IsUsableAmmoForWeapon(weapon, ammoInMag)
            && weapon.GetMagazineSlot() is { } slot
            && slot.CanAccept(mag);
    }

    /// <summary>
    /// Check if this ammo can be used by any equipped weapon.
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
            if (IsUsableAmmoForWeapon(weapon, ammo))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if this magazine can be used by <paramref name="weapon"/>.
    /// </summary>
    public bool IsUsableAmmoForWeapon(Weapon weapon, Ammo ammo)
    {
        foreach (var chamber in weapon.Chambers)
        {
            if (chamber.CanAccept(ammo))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a weapon can be equipped and give the weapon's magazines.
    ///
    /// A bot can equip a weapon if:
    ///   2. We're looting a loose weapon on the world.
    ///   1. The weapon has magazines/loose ammo it can loot from a corpse
    ///
    /// </summary>
    /// <param name="weapon">Weapon to loot.</param>
    /// <param name="corpseEquipment">Corpse inventory to check for magazines.</param>
    /// <param name="usableMagazines">Pre-allocated list for looting found usable magazines.</param>
    public bool IsAbleToEquip(Weapon weapon, Item corpseEquipment, List<Item> usableMagazines)
    {
        if (corpseEquipment is not InventoryEquipment equipment)
        {
            // We're not looting a corpse, just allow to equip
            return true;
        }

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var items);
        equipment.GetAllGridItemsInStorageSlotsNonAlloc(items);

        foreach (var item in items)
        {
            if (item is Magazine mag)
            {
                if (IsUsableMagForWeapon(weapon, mag))
                {
                    usableMagazines.Add(item);
                }
            }
            else if (item is Ammo ammo)
            {
                if (IsUsableAmmoForWeapon(weapon, ammo))
                {
                    usableMagazines.Add(item);
                }
            }
        }

        return usableMagazines.Count > 0;
    }

    /// <summary>
    /// Throws all magazines from the rig that are not used by any of the weapons that the bot currently has equipped.
    /// Also records thrown mag value.
    /// </summary>
    public ValueTask ThrowUselessMagsAsync(Weapon thrownWeapon, Dictionary<Magazine, float> uselessMagazines, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var equipment = _botInventoryController.Inventory.Equipment;
        var primary = equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem as Weapon;
        var hasPrimary = primary is not null;
        var secondary = equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem as Weapon;
        var hasSecondary = secondary is not null;
        var holster = equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon;
        var hasHolster = holster is not null;

        using var pooledList = UnityEngine.Pool.ListPool<Magazine>.Get(out var magazines);
        _botInventoryController.GetAllGridItemsInStorageSlotsNonAlloc(magazines);

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Cleaning up old magazines...");
        }

        var reservedCount = 0;
        foreach (var mag in magazines)
        {
            var fitsInThrown = IsUsableMagForWeapon(thrownWeapon, mag);
            var fitsInPrimary = hasPrimary && IsUsableMagForWeapon(primary, mag);
            var fitsInSecondary = hasSecondary && IsUsableMagForWeapon(secondary, mag);
            var fitsInHolster = hasHolster && IsUsableMagForWeapon(holster, mag);

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
                uselessMagazines.Add(mag, _itemAppraiser.GetItemPrice(mag, _log));
            }
        }

        if (uselessMagazines.Count == 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug("No magazines to clean up");
            }
            return new ValueTask();
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Throwing {uselessMagazines.Count} useless magazines");
        }
        return new ValueTask(
            TransferOrThrowItemsAsync(uselessMagazines, _botInventoryController, _lootingBrain.ActiveLoot.GetRootItem(), token)
        );
    }

    /// <summary>
    /// Try to transfer items to another item's grid, or throw.
    /// Overload that supports taking in a Dictionary with the item's value.
    /// </summary>
    /// <param name="itemsToThrow">A Dictionary of key: items, value: prices to throw.</param>
    /// <param name="previousOwner">The owner of items to throw.</param>
    /// <param name="transferTo">A container to transfer items to.</param>
    public async Task TransferOrThrowItemsAsync<TItem>(
        Dictionary<TItem, float> itemsToThrow,
        IItemOwner previousOwner,
        Item transferTo = null,
        CancellationToken token = default
    )
        where TItem : Item
    {
        var wasPreviousOwner = _botInventoryController == previousOwner;

        foreach (var (item, price) in itemsToThrow)
        {
            token.ThrowIfCancellationRequested();

            if (!await _transactionController.TransferOrThrowItemAsync(item, transferTo, token))
            {
                continue;
            }

            if (wasPreviousOwner)
            {
                _lootingBrain.IgnoreLoot(item.Id);
                Stats.SubtractNetValue(price);
                Stats.AvailableGridSpaces += item.GetItemSize();
                Stats.Gear.TryRemoveContainedItem(item);
            }
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Thrown {item.LocalizedShortName()}{(wasPreviousOwner ? $" (-{price:N0}₽)" : string.Empty)}");
            }
        }
    }

    /// <summary>
    /// Determines the kind of equip action the bot should take when encountering a weapon.
    /// Bots will always prefer to replace weapons that have lower value when encountering a higher value weapon.
    /// </summary>
    public void GetWeaponEquipAction(Weapon lootWeapon, List<LootingAction> lootingActions)
    {
        var equipment = _botInventoryController.Inventory.Equipment;
        var primary = (Weapon)equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        var secondary = (Weapon)equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
        var holster = (Weapon)equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

        var lootValue = CurrentItemPrice;

        // Loot weapon can fit in the holster slot
        // Some mods allow SMGs in the holster.
        if (equipment.GetSlot(EquipmentSlot.Holster).CanAccept(lootWeapon))
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
        var sizeDifference = GetContainerSizeDifference(itemToLoot, equipped);
        if (armorDifference == 0 && sizeDifference > 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Found bigger container {itemToLoot.Name.Localized()} versus {equipped.Name.Localized()}");
            }
            return true;
        }

        // If the item is more valuable than what is equipped, only equip it if the armor class and container size is the same
        if (armorDifference == 0 && sizeDifference == 0 && LootIsMoreValuable(equipped))
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
    /// <returns>Returns true if the item to loot has a larger container than what is equipped</returns>
    public bool LootHasLargerContainer(Item potentialLoot, Item equipped)
    {
        return GetContainerSizeDifference(potentialLoot, equipped) > 0;
    }

    /// <summary>
    /// Calculate the difference between the container sizes of <paramref name="potentialLoot"/> and <paramref name="equipped"/>
    /// </summary>
    /// <returns>Returns a positive integer if the item to loot has a larger container than what is equipped</returns>
    public int GetContainerSizeDifference(Item potentialLoot, Item equipped)
    {
        return potentialLoot.GetContainerSize() - equipped.GetContainerSize();
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

        if (items.Count == 0)
        {
            return true;
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Looting {items.Count} items from {parentItem.Name.Localized()}");
        }

        await LootingTransactionController.SimulatePlayerDelayAsync(LootingBrain.LootingStartDelay, token);
        return await TryAddItemsToBotAsync(items, true, token);
    }

    public async Task<bool> TryNestContainerAsync(SearchableItem item, int itemSize, CancellationToken token = default)
    {
        // BUG: Swapping out item then looting item can cause a ghost loot item in the world
        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Trying to pick up container [{item.LocalizedName()}]...");
        }

        using var pooledUseless = DictionaryPool<Item, float>.Get(out var uselessItems);
        using var pooledOperations = UnityEngine.Pool.ListPool<OperationResult<RemoveResult>>.Get(out var removeOperations);

        // Get undervalued items and remove them so they won't interfere with TryFillContainerAndPickUp, but roll it back afterward
        GetUndervaluedItems(item, uselessItems);
        foreach (var (uselessItem, _) in uselessItems)
        {
            removeOperations.Add(ItemManipulator.RemoveWithoutRestrictions(uselessItem, _botInventoryController));
        }

        var fillResult = ItemManipulatorEx.TryFillContainerAndPickUp(item, _botInventoryController, _transactionController, _log);

        foreach (var removeResult in removeOperations)
        {
            removeResult.Value?.RollBack();
        }

        if (fillResult.Failed)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(fillResult._error);
            }
            return false;
        }

        // Only actually throw them if we succeeded
        await ThrowUndervaluedItemsAsync(item, uselessItems, null, _lootingBrain.ActiveLoot.GetRootItem(), token);

        await LootingTransactionController.SimulatePlayerDelayAsync(LootingBots.TransactionDelay.Value * fillResult.Value.Count, token);

        var networkResult = await fillResult.Value.ExecuteAsync();
        if (networkResult.Failed)
        {
            if (_log.ErrorEnabled)
            {
                _log.LogError(
                    $"Failed to fill container [{item.LocalizedName()}] with items from backpack and pick up. Network Error: {networkResult.Error}"
                );
            }
            return false;
        }

        Stats.AddNetValue(CurrentItemPrice + fillResult.Value.NetWorthDelta);
        Stats.Gear.TryAddContainedItem(item, itemSize, CurrentItemPrice);
        UpdateGridStats();

        if (_log.InfoEnabled)
        {
            _log.LogInfo(
                $"Filled container [{item.LocalizedName()}] with items from backpack and picked up [place: {item.CurrentAddress.GetRootItem()?.Name.Localized()}]"
            );
        }
        return true;
    }

    /// <summary>
    /// Searches through the child items of a container
    /// </summary>
    /// <param name="container">Only throws items of a container of type <see cref="SearchableItem"/></param>
    /// <param name="undervaluedItems"></param>
    public void GetUndervaluedItems(Item container, Dictionary<Item, float> undervaluedItems)
    {
        // Limit to only SearchableItem
        // As opposed to LootNestedItems, we only need to throw away its children if it's a container
        if (container is not SearchableItem)
        {
            return;
        }

        var minimumValue = _isPMC ? LootingBots.PMCMinLootThreshold.Value : LootingBots.ScavMinLootThreshold.Value;

        using var pooledList = UnityEngine.Pool.ListPool<Item>.Get(out var items);
        container.GetAllGridContainedItems(items);
        foreach (var item in items)
        {
            // Check the conditions to filter out items to keep
            if (item.QuestItem || item.IsDogtag() || item is Meds or Money or SearchableItem || (item is Ammo ammo && IsUsableAmmo(ammo)))
            {
                continue;
            }

            if (item is Magazine mag)
            {
                // If it's a magazine we cannot use, throw it
                if (!IsUsableMag(mag))
                {
                    undervaluedItems.Add(mag, _itemAppraiser.GetItemPrice(mag, _log));
                }
                continue;
            }

            var value = _itemAppraiser.GetItemPrice(item, _log);
            if (value < minimumValue)
            {
                undervaluedItems.Add(item, value);
            }
        }
    }

    /// <summary>
    /// Searches through the child items of a container and attempts to throw them.
    /// </summary>
    /// <param name="container">The container from which itemsToThrow came from.</param>
    /// <param name="itemsToThrow">A Dictionary of key: items, value: prices to throw.</param>
    /// <param name="previousOwner">The owner of items to throw.</param>
    /// <param name="transferTo">A container to transfer items to.</param>
    public ValueTask ThrowUndervaluedItemsAsync(
        Item container,
        Dictionary<Item, float> itemsToThrow,
        IItemOwner previousOwner,
        Item transferTo,
        CancellationToken token = default
    )
    {
        if (container is null)
        {
            return new ValueTask();
        }

        token.ThrowIfCancellationRequested();

        if (itemsToThrow.Count == 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No undervalued items found to throw in {container.Name.Localized()}");
            }
            return new ValueTask();
        }

        if (_log.InfoEnabled)
        {
            _log.LogInfo($"Throwing {itemsToThrow.Count} undervalued items from {container.Name.Localized()}");
        }
        return new ValueTask(TransferOrThrowItemsAsync(itemsToThrow, previousOwner, transferTo, token));
    }

    /// <summary>
    /// Strip and loot a weapon's attachments.
    /// </summary>
    public ValueTask<bool> StripWeaponAsync(Weapon weapon, List<Item> modsToLoot, CancellationToken token = default)
    {
        using var pooledList = UnityEngine.Pool.ListPool<Mod>.Get(out var mods);
        weapon.GetAllSlotContainedItems(mods);
        foreach (var mod in mods)
        {
            // Check if the mod's slot is not required, can be modded in raid, and is not a magazine
            if (mod.Parent.Container is Slot { Required: false } && mod is { RaidModdable: true } and not Magazine)
            {
                modsToLoot.Add(mod);
            }
        }

        if (modsToLoot.Count == 0)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No attachments to strip for weapon: {weapon.Name.Localized()}");
            }
            return new ValueTask<bool>(true);
        }

        if (_log.InfoEnabled)
        {
            _log.LogInfo($"Trying to strip attachments of weapon: {weapon.Name.Localized()}");
        }

        // TODO: Mod already looted but still trying to loot its child
        return new ValueTask<bool>(TryAddItemsToBotAsync(modsToLoot, false, token));
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
            || lootItem is SearchableItem searchableItem and not Pockets && CanPickupContainer(searchableItem)
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
    /// Check if this container can be picked up.
    /// </summary>
    /// <param name="container">The container to pick up.</param>
    /// <returns>True if container has a ratio more than <see cref="LootUtils.VEST_GRID_CELL_MIN_RATIO"/> if a Vest, or >1f if a backpack.</returns>
    public bool CanPickupContainer(SearchableItem container)
    {
        var ratio = (float)container.Grids.GetTotalGridSlots() / container.GetItemSize();
        return container switch
        {
            Vest => ratio > LootUtils.VEST_GRID_CELL_MIN_RATIO,
            _ => ratio > 1f,
        };
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
        if (lootItem.IsPlacedInFastAccessSlots() && Stats.Gear.Vest.TryFindReplacement(loot, out index))
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

    public void GetGearAction(Item lootItem, EquipmentSlot slot, List<LootingAction> lootingActions, bool transferItems = false)
    {
        var equippedItem = _botInventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem;
        if (equippedItem is null)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"GetGearAction: Trying to equip {lootItem.Name.Localized()} (₽{CurrentItemPrice:N0})");
            }
            lootingActions.Add(LootingMoveAction.Rent(lootItem, null, CurrentItemPrice + lootItem.GetAllContainedItemsValue(_log)));
            return;
        }

        if (slot != EquipmentSlot.TacticalVest)
        {
            if (ShouldSwapGear(equippedItem, lootItem))
            {
                GetSwapAction(lootItem, equippedItem, lootingActions, transferItems);
                if (transferItems)
                {
                    // Has to be outside GetSwapAction to not conflict with Vest logic below
                    lootingActions.Add(LootingLootAction.Rent(equippedItem, this));
                }
            }
            return;
        }

        // Gear action for vest
        if (ShouldSwapGear(equippedItem, lootItem))
        {
            // If we have a chest armor equipped and the tac vest we are looting is armored,
            // check if the armored rig is higher armor class than the chest,
            // then make sure to drop the chest and pick up the armored rig
            var chest = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem;
            if (chest is not null && EquipmentTypeUtils.IsArmoredRig(lootItem))
            {
                if (ShouldSwapGear(chest, lootItem))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug(
                            $"Trying to drop chest armor [{chest.Name.Localized()}] then loot armored rig [{lootItem.Name.Localized()}]"
                        );
                    }

                    lootingActions.Add(LootingThrowAction.Rent(chest, -_itemAppraiser.GetItemPrice(chest, _log)));
                    GetSwapAction(lootItem, equippedItem, lootingActions, true);
                    lootingActions.Add(LootingLootAction.Rent(equippedItem, this));
                    lootingActions.Add(LootingLootAction.Rent(chest, this));
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
                GetSwapAction(lootItem, equippedItem, lootingActions, true);
                lootingActions.Add(LootingLootAction.Rent(equippedItem, this));
            }
        }
        else if (
            EquipmentTypeUtils.IsArmoredRig(equippedItem) && _lootingBrain.ActiveLoot.GetRootItem() is InventoryEquipment corpseEquipment
        )
        {
            // At this point, the bot has an equipped armored rig so it won't be swapping for a normal rig,
            // so if:
            //   1. Corpse armor is better armor,
            //   2. OR same armor but corpse rig has larger container than equipped rig,
            //   3. OR same armor and same sized container but more valuable.
            // Drop the current armored rig then loot the armor and tac vest.
            // Same with ShouldSwapGear, but chest armor is compared with the armored rig, and size and price is compared with the vest(lootItem).
            var corpseChestArmor = corpseEquipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem;
            if (corpseChestArmor is null || !AllowedToEquip(corpseChestArmor))
            {
                return;
            }
            var shouldSwap = false;

            var armorDifference = GetArmorDifference(corpseChestArmor, equippedItem);
            if (armorDifference > 0)
            {
                shouldSwap = true;
            }
            var containerDifference = GetContainerSizeDifference(lootItem, equippedItem);
            if (armorDifference == 0 && containerDifference > 0)
            {
                shouldSwap = true;
            }
            if (armorDifference == 0 && containerDifference == 0 && LootIsMoreValuable(equippedItem))
            {
                shouldSwap = true;
            }

            if (!shouldSwap)
            {
                return;
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Trying to loot chest armor [{corpseChestArmor.Name.Localized()}] and tac vest [{lootItem.Name.Localized()}] and drop current armored rig [{equippedItem.Name.Localized()}]"
                );
            }

            // Throw the corpse's chest armor so we can swap the vests
            lootingActions.Add(LootingThrowAction.Rent(corpseChestArmor, 0f, false));
            GetSwapAction(lootItem, equippedItem, lootingActions, true);
            lootingActions.Add(LootingMoveAction.Rent(corpseChestArmor, null, _itemAppraiser.GetItemPrice(corpseChestArmor, _log)));
            lootingActions.Add(LootingLootAction.Rent(equippedItem, this));
        }
    }

    /// <summary>
    /// Generates a SwapAction to be executed by the transaction controller.
    /// </summary>
    public void GetSwapAction(Item lootItem, Item equippedItem, List<LootingAction> lootingActions, bool transferItems)
    {
        var toEquipValue = CurrentItemPrice;
        var toSwapValue = _itemAppraiser.GetItemPrice(equippedItem, _log);

        if (_log.DebugEnabled)
        {
            _log.LogDebug(
                $"Trying to equip {lootItem.Name.Localized()} (₽{toEquipValue:N0}) and swap with {equippedItem.Name.Localized()} (₽{toSwapValue:N0}){(transferItems ? $" then loot {equippedItem.Name.Localized()}" : string.Empty)}"
            );
        }

        // Include contained items in calculating NetWorthDelta
        toEquipValue += lootItem.GetAllContainedItemsValue(_log);
        toSwapValue += equippedItem.GetAllContainedItemsValue(_log);

        lootingActions.Add(LootingSwapAction.Rent(lootItem, equippedItem, toEquipValue - toSwapValue, transferItems));
    }

    public void IgnoreLoot(string id)
    {
        _lootingBrain.IgnoreLoot(id);
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
