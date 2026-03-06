using System.Text;
using Cysharp.Threading.Tasks;
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

    public void UpdatePair(ValuePair pair)
    {
        Id = pair.Id;
        Value = pair.Value;
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
        Color freeSpaceColor =
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

    public readonly BotStats Stats = new();

    public ArmorComponent CurrentArmorVest
    {
        get
        {
            Item chest = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmorVest).ContainedItem;
            return chest?.GetItemComponent<ArmorComponent>();
        }
    }

    public ArmorComponent CurrentArmorRig
    {
        get
        {
            SearchableItemItemClass tacVest = (SearchableItemItemClass)
                _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
            return tacVest?.GetItemComponent<ArmorComponent>();
        }
    }

    public ArmorComponent CurrentHeadArmor
    {
        get
        {
            Item helmet = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem;
            return helmet?.GetItemComponent<ArmorComponent>();
        }
    }

    public ArmorComponent CurrentTorsoArmor
    {
        get { return CurrentArmorRig ?? CurrentArmorVest; }
    }

    public int CurrentTorsoArmorClass
    {
        get { return CurrentTorsoArmor?.ArmorClass ?? 0; }
    }

    public int CurrentHeadArmorClass
    {
        get { return CurrentHeadArmor?.ArmorClass ?? 0; }
    }

    // Represents the value in roubles of the current item
    public float CurrentItemPrice;

    public bool ShouldSort = true;

    public LootingInventoryController(BotOwner botOwner, LootingBrain lootingBrain)
    {
        _log = new BotLog(LootingBots.LootLog, botOwner);

        try
        {
            _lootingBrain = lootingBrain;
            _itemAppraiser = LootingBots.ItemAppraiser;

            // Initialize bot inventory controller
            _botInventoryController = botOwner.GetPlayer.InventoryController;
            _botOwner = botOwner;
            _transactionController = new LootingTransactionController(_botInventoryController, _log);

            CalculateGearValue();
            CalculateInitialNetWorth();
            UpdateGridStats();
        }
        catch (Exception e)
        {
            _log.LogError(e);
        }
    }

    /**
    * Calculates the value of the bot's current weapons to use in weapon swap comparison checks
    */
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
        Stats.NetWorth = 0f;
        foreach (var slot in _botInventoryController.Inventory.Equipment.CachedSlots)
        {
            var containedItem = slot.ContainedItem;
            if (containedItem == null)
            {
                continue;
            }

            if (containedItem is SearchableItemItemClass searchableItem)
            {
                foreach (var nestedItem in searchableItem.GetFirstLevelItems())
                {
                    Stats.NetWorth += _itemAppraiser.GetItemPrice(nestedItem, _log);
                }
            }
            else
            {
                Stats.NetWorth += _itemAppraiser.GetItemPrice(containedItem, _log);
            }
        }
        Stats.InitialNetWorth = Stats.NetWorth;
    }

    /**
    * Updates stats for AvailableGridSpaces and TotalGridSpaces based off the bots current gear
    */
    public void UpdateGridStats()
    {
        SearchableItemItemClass tacVest = (SearchableItemItemClass)
            _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
        SearchableItemItemClass backpack = (SearchableItemItemClass)
            _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
        SearchableItemItemClass pockets = (SearchableItemItemClass)
            _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;

        int freePockets = LootUtils.GetAvailableGridSlots(pockets?.Grids);
        int freeTacVest = LootUtils.GetAvailableGridSlots(tacVest?.Grids);
        int freeBackpack = LootUtils.GetAvailableGridSlots(backpack?.Grids);

        Stats.AvailableGridSpaces = freeBackpack + freePockets + freeTacVest;
        Stats.TotalGridSpaces = (tacVest?.Grids?.Length ?? 0) + (backpack?.Grids?.Length ?? 0) + (pockets?.Grids?.Length ?? 0);
    }

    // /**
    // * Sorts the items in the tactical vest so that items prefer to be in slots that match their size. I.E a 1x1 item will be placed in a 1x1 slot instead of a 1x2 slot
    // */
    // public async UniTask SortTacVestAsync()
    // {
    //     SearchableItemItemClass tacVest = (SearchableItemItemClass)
    //         _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
    //
    //     ShouldSort = false;
    //
    //     if (tacVest != null)
    //     {
    //         var result = InteractionsHandlerClass.Sort(tacVest, _botInventoryController, true);
    //         await UniTask.Yield();
    //
    //         if (result.Succeeded)
    //         {
    //             try
    //             {
    //                 await _transactionController.TryRunNetworkTransactionAsync(result);
    //             }
    //             catch (Exception ex)
    //             {
    //                 _log.LogError($"Failed to execute {nameof(SortTacVestAsync)}: {ex}");
    //             }
    //         }
    //         else if (_log.ErrorEnabled)
    //         {
    //             _log.LogError($"Failed to execute {nameof(SortTacVestAsync)}: {result.Error}");
    //         }
    //     }
    // }

    /**
    * Main driving method which kicks off the logic for what a bot will do with the loot found.
    * If bots are looting something that is equippable and they have nothing equipped in that slot, they will always equip it.
    * If the bot decides not to equip the item then it will attempt to put in an available container slot
    */
    public async UniTask<bool> TryAddItemsToBotAsync(List<Item> items, CancellationToken token = default)
    {
        List<LootingAction> lootingActions = ListActionPool.Rent();
        try
        {
            foreach (Item item in items)
            {
                token.ThrowIfCancellationRequested();

                if (item.Name == null)
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

                if (_log.InfoEnabled)
                {
                    var itemValue = itemSize > 1
                        ? $"{CurrentItemPrice:N0}₽ {CurrentItemPrice / itemSize:N0}₽/slot"
                        : $"{CurrentItemPrice:N0}₽";
                    _log.LogInfo($"Loot found: {itemName} ({itemValue})");
                }

                // Ignore magazines that a bot cannot actively use
                if (item is MagazineItemClass mag && !IsUsableMag(mag))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Cannot use mag: {itemName}. Skipping");
                    }

                    continue;
                }

                // Check to see if we need to swap gear
                lootingActions.Clear();
                var canEquipGear = GetEquipAction(item, lootingActions);
                if (canEquipGear)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Found equip action for: {itemName}");
                    }

                    foreach (var action in lootingActions)
                    {
                        // Wait if bot is busy
                        await UniTask.WaitWhile(_botInventoryController, static invCont => invCont.HasAnyHandsActionNonLinq(), cancellationToken: token);

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
                                // To make space we:
                                // - throw away useless mags in our newly equipped item
                                // - throw undervalued items in our newly equipped item
                                // Then loot the thrown item
                                await ThrowUselessMagsAsync(null, token);
                                await ThrowUndervaluedItemsAsync((SearchableItemItemClass) swapAction.Item, token);
                                await LootNestedItemsAsync((SearchableItemItemClass) swapAction.ToSwap, token);
                            }
                            if (swapAction.ThrowMags && swapAction.ToSwap is Weapon thrownWeapon)
                            {
                                // If we swapped away our previous weapon, throw away its mags
                                await ThrowUselessMagsAsync(thrownWeapon, token);
                            }
                        }
                        else if (action is LootingThrowAction throwAction)
                        {
                            // Ignore thrown loot
                            _lootingBrain.IgnoreLoot(throwAction.Item.Id);
                        }
                    }

                    // Do post-equip actions
                    // We looted a weapon, change to primary and calculate gear value
                    if (item is Weapon)
                    {
                        await ChangeToPrimaryAsync(token);
                        RefillAndReload();
                        CalculateGearValue();
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
                if (item is SearchableItemItemClass searchableItem)
                {
                    bool success = await LootNestedItemsAsync(searchableItem, token);

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
                    var itemsToAdd = ListPool<Item>.Get();
                    try
                    {
                        foreach (Slot weaponSlot in weapon.Slots)
                        {
                            if (!weaponSlot.Required)
                            {
                                foreach (Item weaponMod in weaponSlot.Items)
                                {
                                    // check if the weaponMod is an actual mod and if it can be modded in raid
                                    if (weaponMod is Mod mod && mod.RaidModdable)
                                    {
                                        itemsToAdd.Add(weaponMod);
                                    }
                                }
                            }
                        }
                        if (itemsToAdd.Count > 0)
                        {
                            if (_log.DebugEnabled)
                            {
                                _log.LogDebug($"Trying to strip attachments of weapon: {weapon.Name.Localized()}");
                            }

                            // Call TryAddItemsToBot with the filtered items
                            bool success = await TryAddItemsToBotAsync(itemsToAdd, token);
                            if (!success)
                            {
                                return false;
                            }
                        }
                    }
                    finally
                    {
                        if (token.IsCancellationRequested)
                        {
                            _log.LogError("TryAddItemsToBotAsync:Cancelled, returning List<Item> to ListPool<Item>");
                        }
                        ListPool<Item>.Release(itemsToAdd);
                    }
                }
            }
        }
        finally
        {
            if (token.IsCancellationRequested)
            {
                _log.LogError("TryAddItemsToBotAsync:Cancelled, returning List<LootingAction> to ListActionPool");
            }
            ListActionPool.Return(lootingActions);
        }

        return true;
    }

    /** Use the ExamineTime of an object and the AttentionExamineValue of the bot to calculate the delay for discovering an item while looting */
    public UniTask SimulateExamineTimeAsync(Item item, CancellationToken token = default)
    {
        // Taken from ExamineOperationClass constructor
        return LootingTransactionController.SimulatePlayerDelayAsync(
            item.ExamineTime * 1000f / (1f + _botOwner.Profile.Skills.AttentionExamineValue),
            token
        );
    }

    /**
    * Method to make the bot change to its primary weapon. Useful for making sure bots have their weapon out after they have swapped weapons.
    */
    public UniTask ChangeToPrimaryAsync(CancellationToken token)
    {
        if (_log.DebugEnabled)
        {
            _log.LogDebug("Changing to primary");
        }

        // _botOwner.GetPlayer.HandsController.FastForwardCurrentState();
        _botOwner.WeaponManager.UpdateWeaponsList();
        return UniTask.WaitUntil(_botOwner.WeaponManager.Selector, static selector => selector.ChangeToMain(), cancellationToken: token);
    }

    /**
    * Updates the bot's known weapon list and tells the bot to switch to its main weapon
    */
    public void UpdateActiveWeapon()
    {
        // if (_botOwner != null && _botOwner.WeaponManager?.Selector != null)
        // {
        //     if (_log.InfoEnabled)
        //     {
        //         _log.LogInfo("Updating weapons");
        //     }
        //
        //     _botOwner.GetPlayer.HandsController.FastForwardCurrentState();
        //     _botOwner.WeaponManager.UpdateWeaponsList();
        //     _botOwner.WeaponManager.Selector.TakeMainWeapon();
        //     RefillAndReload();
        // }
    }

    /**
    * Method to refill magazines with ammo and also reload the current weapon with a new magazine
    */
    private void RefillAndReload()
    {
        // Is already done by Selector.ChangeToMain
        // _botOwner.WeaponManager.Reload?.TryFillMagazines();

        _botOwner.WeaponManager.Reload?.TryReload();
    }

    /**
    * Checks certain slots to see if the item we are looting is "better" than what is currently equipped. View shouldSwapGear for criteria.
    * Gear is checked in a specific order so that bots will try to swap gear that is a "container" first like backpacks and tacVests to make sure
    * they arent putting loot in an item they will ultimately decide to drop
    */
    public bool GetEquipAction(Item lootItem, List<LootingAction> lootingActions)
    {
        if (!AllowedToEquip(lootItem))
        {
            return false;
        }

        if (lootItem.Template is WeaponTemplate && !BotTypeUtils.IsBoss(_botOwner.Profile.Info.Settings.Role))
        {
            GetWeaponEquipAction(lootItem as Weapon, lootingActions);
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
            GetSwapAction(lootItem, helmet, lootingActions);
        }
        else if (EquipmentTypeUtils.IsEarpiece(lootItem) && ShouldSwapGear(earpiece, lootItem))
        {
            GetSwapAction(lootItem, earpiece, lootingActions);
        }
        else if (EquipmentTypeUtils.IsFaceCover(lootItem) && ShouldSwapGear(faceCover, lootItem))
        {
            GetSwapAction(lootItem, faceCover, lootingActions);
        }
        else if (EquipmentTypeUtils.IsEyewear(lootItem) && ShouldSwapGear(eyewear, lootItem))
        {
            GetSwapAction(lootItem, eyewear, lootingActions);
        }
        else if (EquipmentTypeUtils.IsArmband(lootItem) && ShouldSwapGear(armBand, lootItem))
        {
            GetSwapAction(lootItem, armBand, lootingActions);
        }
        else if (EquipmentTypeUtils.IsChestArmor(lootItem) && ShouldSwapGear(chest, lootItem))
        {
            GetSwapAction(lootItem, chest, lootingActions);
        }
        else if (EquipmentTypeUtils.IsTacticalRig(lootItem) && ShouldSwapGear(tacVest, lootItem))
        {
            // If the tac vest we are looting is higher armor class, and we have a chest equipped,
            // check if the tac vest is higher armor class than the chest,
            // then make sure to drop the chest and pick up the armored rig
            if (chest != null)
            {
                if (GetArmorDifference(chest, lootItem) > 0)
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
            }
            else
            {
                GetSwapAction(lootItem, tacVest, lootingActions, true);
            }
        }

        return lootingActions.Count > 0;
    }

    public bool IsUsableMag(MagazineItemClass mag)
    {
        return mag != null && HasAcceptableMagazineSlot(_botInventoryController.Inventory.Equipment, mag);
    }

    private static readonly EquipmentSlot[] _weaponSlots = [EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster];

    private static bool HasAcceptableMagazineSlot(InventoryEquipment equipment, MagazineItemClass mag)
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

    private readonly List<MagazineItemClass> _throwUselessMagsScratch = [];

    /**
    * Throws all magazines from the rig that are not used by any of the weapons that the bot currently has equipped.
    * Also records thrown mag value.
    */
    public async UniTask ThrowUselessMagsAsync(Weapon thrownWeapon, CancellationToken token)
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

        int reservedCount = 0;
        foreach (MagazineItemClass mag in _throwUselessMagsScratch)
        {
            var fitsInThrown = thrownMagSlot?.CanAccept(mag) == true;
            var fitsInPrimary = primaryMagSlot?.CanAccept(mag) == true;
            var fitsInSecondary = secondaryMagSlot?.CanAccept(mag) == true;
            var fitsInHolster = holsterMagSlot?.CanAccept(mag) == true;

            bool fitsInEquipped = fitsInPrimary || fitsInSecondary || fitsInHolster;
            bool isSharedMag = fitsInThrown && fitsInEquipped;
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
            }
        }

        if (_log.DebugEnabled)
        {
            _log.LogDebug("Cleaning up old mags...done");
        }
    }

    /**
    * Determines the kind of equip action the bot should take when encountering a weapon. Bots will always prefer to replace weapons that have lower value when encountering a higher value weapon.
    */
    public void GetWeaponEquipAction(Weapon lootWeapon, List<LootingAction> lootingActions)
    {
        Weapon primary = (Weapon) _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        Weapon secondary = (Weapon) _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
        Weapon holster = (Weapon) _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

        bool isPistol = lootWeapon.WeapClass.Equals("pistol");
        float lootValue = CurrentItemPrice;

        if (isPistol)
        {
            if (holster == null)
            {
                var holsterPlace = _botInventoryController.FindSlotToPickUp(lootWeapon);
                if (holsterPlace != null)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to holster");
                    }

                    var moveAction = LootingMoveAction.Rent(lootWeapon, holsterPlace, lootValue);
                    lootingActions.Add(moveAction);
                }
            }
            else
            {
                var holsterValue = Stats.WeaponValues.Holster.Value;
                if (lootValue > holsterValue)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Trying to swap {holster.Name.Localized()} (₽{holsterValue}) with {lootWeapon.Name.Localized()} (₽{lootValue}) in holster");
                    }

                    var swapAction = LootingSwapAction.Rent(lootWeapon, holster, lootValue - holsterValue, true);
                    lootingActions.Add(swapAction);
                }
            }
        }
        else
        {
            var primaryValue = Stats.WeaponValues.Primary.Value;
            var isBetterThanPrimary = lootValue > primaryValue;

            var secondaryValue = Stats.WeaponValues.Secondary.Value;
            var isBetterThanSecondary = lootValue > secondaryValue;

            // If we have no primary, just equip the weapon to primary
            if (primary == null)
            {
                var primaryPlace = _botInventoryController.FindSlotToPickUp(lootWeapon);
                if (primaryPlace != null)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to primary");
                    }

                    var moveAction = LootingMoveAction.Rent(lootWeapon, primaryPlace, lootValue);
                    lootingActions.Add(moveAction);
                }
            }
            // If the weapon is better than primary
            else
            {
                if (isBetterThanPrimary)
                {
                    // If the weapon is better than the primary and there is no secondary,
                    // move the loot weapon to secondary, then equip the loot weapon by swapping the loot weapon (in secondary) with primary
                    if (secondary == null)
                    {
                        ItemAddress secondaryPlace = _botInventoryController.FindSlotToPickUp(primary);
                        if (secondaryPlace != null)
                        {
                            if (_log.DebugEnabled)
                            {
                                _log.LogDebug($"Trying to move {lootWeapon.Name.Localized()} (₽{lootValue}) to secondary and swapping with primary {primary.Name.Localized()} (₽{primaryValue})");
                            }

                            var equipAction = LootingMoveAction.Rent(lootWeapon, secondaryPlace, lootValue);
                            lootingActions.Add(equipAction);

                            var swapAction = LootingSwapAction.Rent(lootWeapon, primary);
                            lootingActions.Add(swapAction);
                        }
                    }
                    // If the weapon is also better than the secondary,
                    // swap the loot weapon with secondary, then equip the loot weapon by swapping the loot weapon (in secondary) with primary
                    else if (isBetterThanSecondary)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug($"Trying to swap {secondary.Name.Localized()} (₽{secondaryValue}) with {primary.Name.Localized()} (₽{primaryValue}) and equip {lootWeapon.Name.Localized()} (₽{lootValue})");
                        }

                        // TODO: Might have to throw secondary instead?
                        // Check if parent is a slot or a grid
                        var swapAction = LootingSwapAction.Rent(lootWeapon, secondary, lootValue - secondaryValue, true);
                        lootingActions.Add(swapAction);

                        var equipToMainAction = LootingSwapAction.Rent(lootWeapon, primary);
                        lootingActions.Add(equipToMainAction);
                    }
                }
                // If there is no secondary weapon, equip to secondary
                else if (secondary == null)
                {
                    var secondaryPlace = _botInventoryController.FindSlotToPickUp(lootWeapon);
                    if (secondaryPlace != null)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug($"Trying to equip {lootWeapon.Name.Localized()} (₽{lootValue}) to secondary");
                        }

                        var moveAction = LootingMoveAction.Rent(lootWeapon, secondaryPlace, lootValue);
                        lootingActions.Add(moveAction);
                    }
                }
                // If the loot weapon is worth more than the secondary, swap it
                else if (isBetterThanSecondary)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Trying to swap {secondary.Name.Localized()} (₽{secondaryValue}) with {lootWeapon.Name.Localized()} (₽{lootValue}) in secondary");
                    }

                    // TODO: Might have to throw secondary instead?
                    // Check if parent is a slot or a grid
                    var swapAction = LootingSwapAction.Rent(lootWeapon, secondary, lootValue - secondaryValue, true);
                    lootingActions.Add(swapAction);
                }
            }
        }
    }

    /**
    * Checks to see if the bot should swap its currently equipped gear with the item to loot. Bot will swap under the following criteria:
    * 1. The item is a container and its larger than what is equipped.
    *   - Tactical rigs have an additional check, will not switch out if the rig we are looting is lower armor class than what is equipped
    * 2. The item has an armor rating, and its higher than what is currently equipped.
    */
    public bool ShouldSwapGear(Item equipped, Item itemToLoot)
    {
        if (equipped == null)
        {
            return false;
        }

        // Bosses cannot swap gear as many bosses have custom logic tailored to their loadouts
        if (BotTypeUtils.IsBoss(_botOwner.Profile.Info.Settings.Role))
        {
            return false;
        }

        bool foundBiggerContainer = false;

        // If the item is a container, calculate the size and see if its bigger than what is equipped
        if (equipped.IsContainer)
        {
            int equippedSize = (equipped as SearchableItemItemClass).GetContainerSize();
            int itemToLootSize = (itemToLoot as SearchableItemItemClass).GetContainerSize();

            foundBiggerContainer = equippedSize < itemToLootSize;
        }

        int armorDifference = GetArmorDifference(equipped, itemToLoot);

        bool foundBetterArmor = armorDifference > 0; // Equip if we found item with a better armor class.
        bool pickupBiggerContainer = armorDifference == 0 && foundBiggerContainer; // If the item is bigger than what is equipped, only equip it if the armor class is the same

        if (foundBetterArmor || pickupBiggerContainer)
        {
            return true;
        }

        return armorDifference == 0 && LootIsMoreValuable(equipped); // If the item is more valuable than what is equipped, only equip it if the armor class is the same
    }

    /** Given a piece of armor, compare it against what is curren */
    public bool IsBetterArmorThanEquipped(ArmoredEquipmentItemClass newArmor)
    {
        ArmorComponent equippedArmor = EquipmentTypeUtils.IsHelmet(newArmor) ? CurrentHeadArmor : CurrentTorsoArmor;
        return GetArmorDifference(equippedArmor?.Item, newArmor) > 0;
    }

    /** Compare equipped value with current item price (itemToLoot) */
    private bool LootIsMoreValuable(Item equippedItem)
    {
        return CurrentItemPrice > LootingBots.ItemAppraiser.GetItemPrice(equippedItem, _log);
    }

    /**
    * Returns an integer representing the difference between the armor classes of the itemToLoot and the currently equippedItem
    */
    public static int GetArmorDifference(Item equippedItem, Item itemToLoot)
    {
        ArmorComponent newArmor = itemToLoot.GetItemComponent<ArmorComponent>();
        ArmorComponent currentArmor = equippedItem?.GetItemComponent<ArmorComponent>();

        int currentArmorClass = currentArmor?.ArmorClass ?? 0;
        int newArmorClass = newArmor?.ArmorClass ?? 0;

        return newArmorClass - currentArmorClass;
    }

    /** Searches throught the child items of a container and attempts to loot them */
    public async UniTask<bool> LootNestedItemsAsync(SearchableItemItemClass parentItem, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var items = ListPool<Item>.Get();
        try
        {
            foreach (var nestedItem in parentItem.GetFirstLevelItems())
            {
                // Check the conditions to filter out items
                bool isItemLocked = nestedItem.CurrentAddress?.Container is Slot slot && slot.Locked;

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
                _log.LogDebug($"No nested items found in {parentItem.Name}");
            }

            return true;
        }
        finally
        {
            if (token.IsCancellationRequested)
            {
                _log.LogError("LootNestedItemsAsync:Cancelled, returning List<Item> to ListPool<Item>");
            }
            ListPool<Item>.Release(items);
        }
    }

    /** Searches through the child items of a container and attempts to throw them */
    public async UniTask ThrowUndervaluedItemsAsync(SearchableItemItemClass parentItem, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var itemsToThrow = DictionaryPool<Item, float>.Get();
        try
        {
            var botType = _botOwner.Profile.Info.Settings.Role;
            var isPmc = botType.IsPMC();

            foreach (var nestedItem in parentItem.GetFirstLevelItems())
            {
                // Check the conditions to filter out items
                if (nestedItem.Id == parentItem.Id ||
                    nestedItem is MagazineItemClass || // Use ThrowUselessMagazinesAsync
                    nestedItem.QuestItem ||
                    nestedItem.CurrentAddress?.Container is Slot slot && slot.Locked) // Slot is locked
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
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Throwing {itemsToThrow.Count} undervalued items from {parentItem.Name.Localized()}");
                }

                foreach ((Item item, float value) in itemsToThrow)
                {
                    await LootingTransactionController.SimulatePlayerDelayAsync(token: token);

                    if (await _transactionController.ThrowItemAsync(item, token))
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug($"Thrown {item.Name.Localized()} (-{value:N0}₽)");
                        }
                        Stats.SubtractNetValue(value);
                    }
                }

                return;
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No nested items found to throw in {parentItem.Name}");
            }
        }
        finally
        {
            if (token.IsCancellationRequested)
            {
                _log.LogError("ThrowUndervaluesItemsAsync:Cancelled, returning Dictionary<Item, float> to DictionaryPool<Item, float>");
            }
            DictionaryPool<Item, float>.Release(itemsToThrow);
        }
    }

    /**
        Check if the item being looted meets the loot value threshold specified in the mod settings and saves its value in CurrentItemPrice.
        PMC bots use the PMC loot threshold, all other bots such as scavs, bosses, and raiders will use the scav threshold
    */
    public bool IsValuableEnough(float itemPrice)
    {
        WildSpawnType botType = _botOwner.Profile.Info.Settings.Role;
        bool isPMC = botType.IsPMC();

        // If the bot is a PMC, compare the price against the PMC loot threshold. For all other bot types use the scav threshold
        float min = (isPMC ? LootingBots.PMCMinLootThreshold : LootingBots.ScavMinLootThreshold).Value;
        float max = (isPMC ? LootingBots.PMCMaxLootThreshold : LootingBots.ScavMaxLootThreshold).Value;

        // If max is set to 0, do not check agains max threshold
        return itemPrice >= min && (max == 0f || itemPrice <= max);
    }

    public bool AllowedToEquip(Item lootItem)
    {
        EquipmentType eligiblePmcGear = (EquipmentType) LootingBots.PMCGearToEquip.Value;
        EquipmentType eligibleScavGear = (EquipmentType) LootingBots.ScavGearToEquip.Value;

        WildSpawnType botType = _botOwner.Profile.Info.Settings.Role;
        bool isPMC = botType.IsPMC();
        bool allowedToEquip = isPMC ? eligiblePmcGear.IsItemEligible(lootItem) : eligibleScavGear.IsItemEligible(lootItem);

        return allowedToEquip;
    }

    public bool AllowedToPickup(Item lootItem, int itemSize = 1)
    {
        WildSpawnType botType = _botOwner.Profile.Info.Settings.Role;
        bool isPMC = botType.IsPMC();
        bool pickupNotRestricted = isPMC
            ? LootingBots.PMCGearToPickup.Value.IsItemEligible(lootItem, true)
            : LootingBots.ScavGearToPickup.Value.IsItemEligible(lootItem, true);
        bool isMoney = lootItem.Template is MoneyTemplateClass;

        // All usable mags and money should be considered eligible to loot. Otherwise all other items fall subject to the mod settings for restricting pickup and loot value thresholds
        return IsUsableMag(lootItem as MagazineItemClass)
               || isMoney
               || (pickupNotRestricted && (EquipmentTypeUtils.IsDogtag(lootItem) || IsValuableEnough(CurrentItemPrice / itemSize /* Divide by slots to get price per slot */)));
    }

    /** Generates a SwapAction to send to the transaction controller*/
    public void GetSwapAction(
        Item toEquip,
        Item toSwap,
        List<LootingAction> lootingActions,
        bool transferItems = false
    )
    {
        var toEquipValue = CurrentItemPrice;
        var toSwapValue = _itemAppraiser.GetItemPrice(toSwap, _log);
        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Trying to equip {toEquip.Name.Localized()} (₽{toEquipValue:N0}) and swap with {toSwap.Name.Localized()} (₽{toSwapValue:N0}){(transferItems ? $" then loot {toSwap.Name.Localized()}" : string.Empty)}");
        }

        var swapAction = LootingSwapAction.Rent(toEquip, toSwap, toEquipValue - toSwapValue, false, transferItems);
        lootingActions.Add(swapAction);
    }
}
