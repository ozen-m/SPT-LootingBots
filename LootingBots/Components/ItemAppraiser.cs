using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using UnityEngine;

namespace LootingBots.Components;

public class ItemAppraiser(Log _log)
{
    public readonly Stopwatch LastPriceUpdate = Stopwatch.StartNew();

    public Dictionary<MongoID, HandbookData> HandbookData;
    public Dictionary<MongoID, float> MarketData;

    public bool IsUpdatingPrices { get; private set; }

    public async Task UpdatePricesAsync()
    {
        IsUpdatingPrices = true;
        try
        {
            if (LootingBots.UseMarketPrices.Value)
            {
                var tcs = new TaskCompletionSource<Result<Dictionary<string, float>>>();
                Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession().RagfairGetPrices(tcs.SetResult);
                var ragfairPrices = await tcs.Task;
                if (ragfairPrices.Succeed)
                {
                    MarketData = ragfairPrices.Value.ToDictionary(pair => new MongoID(pair.Key), pair => pair.Value);
                }
                if (MarketData is null)
                {
                    _log.LogError("Failed to get flea prices from BE session");
                }
            }
            else
            {
                // This is the handbook instance which is initialized when the client first starts.
                HandbookData = Singleton<Handbook>.Instance.Items.ToDictionary(item => new MongoID(item.Id));
                if (HandbookData is null)
                {
                    _log.LogError("Failed to get handbook data");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex.ToString());
            _log.LogError("Failed to get item prices");
        }
        finally
        {
            LastPriceUpdate.Restart();
            IsUpdatingPrices = false;
        }
    }

    /// <summary>
    /// Will either get the lootItem's price using the ragfair service or the handbook depending on the option selected in the mod menu.
    /// If the item is a weapon, will calculate its value based off its attachments if the mod setting is enabled.
    /// If the item is an armor, will calculate its value based off its slots containing plates/shields if the mod setting is enabled.
    /// </summary>
    public float GetItemPrice(Item lootItem, BotLog log)
    {
        // Get the price of an ammo box by its ammo
        if (lootItem is AmmoBox box)
        {
            var ammoItem = box.Cartridges.Items.GetFirstItem();
            if (ammoItem != null)
            {
                lootItem = ammoItem;
            }
        }

        if (LootingBots.UseMarketPrices.Value && MarketData != null)
        {
            if (lootItem is Weapon weapon && LootingBots.ValueFromMods.Value)
            {
                return GetWeaponMarketPrice(weapon, log);
            }
            if (LootingBots.ValueFromPlates.Value && lootItem.TryGetItemComponent(out ArmorHolderComponent _))
            {
                return GetArmorMarketPrice((CompoundItem)lootItem, log);
            }
            return GetItemMarketPrice(lootItem, log);
        }

        if (HandbookData != null)
        {
            if (lootItem is Weapon weapon && LootingBots.ValueFromMods.Value)
            {
                return GetWeaponHandbookPrice(weapon, log);
            }
            if (LootingBots.ValueFromPlates.Value && lootItem.TryGetItemComponent(out ArmorHolderComponent _))
            {
                return GetArmorHandbookPrice((CompoundItem)lootItem, log);
            }
            return GetItemHandbookPrice(lootItem, log);
        }

        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug("ItemAppraiser data is null");
            }
            else
            {
                _log.LogDebug("ItemAppraiser data is null");
            }
        }

        return 0f;
    }

    /// <summary>
    /// Get the price of a weapon from the sum of its attachments mods, using the default handbook prices to appraise each mod.
    /// </summary>
    public float GetWeaponHandbookPrice(Weapon lootWeapon, BotLog log)
    {
        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }
            else
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }
        }

        var finalPrice = 0f;

        foreach (var weaponMod in lootWeapon.Mods)
        {
            finalPrice += GetItemHandbookPrice(weaponMod, log);
        }

        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Final price of attachments: {finalPrice} compared to full item {GetItemHandbookPrice(lootWeapon, log)}");
            }
            else
            {
                _log.LogDebug($"Final price of attachments: {finalPrice} compared to full item {GetItemHandbookPrice(lootWeapon, null)}");
            }
        }

        return finalPrice;
    }

    /// <summary>
    /// Get the price of an armor from the sum of its slots containing armor items, using the default handbook prices to appraise each plate.
    /// </summary>
    public float GetArmorHandbookPrice(CompoundItem lootArmor, BotLog log)
    {
        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Getting value of armor {lootArmor.Name.Localized()}");
            }
            else
            {
                _log.LogDebug($"Getting value of armor {lootArmor.Name.Localized()}");
            }
        }

        var finalPrice = 0f;

        // These don't have movable plates, but can have faceshields
        if (lootArmor is Headwear or FaceCover)
        {
            finalPrice += GetItemHandbookPrice(lootArmor, log);
        }

        // Iterate over each armor plate and accumulate the price
        foreach (var slot in lootArmor.Slots)
        {
            if (slot.ContainedItem is ArmoredEquipment armoredEquipment)
            {
                finalPrice += GetItemHandbookPrice(armoredEquipment, log);
            }
        }

        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Final price of armor: {finalPrice} compared to item template {GetItemHandbookPrice(lootArmor, log)}");
            }
            else
            {
                _log.LogDebug($"Final price of armor: {finalPrice} compared to item template {GetItemHandbookPrice(lootArmor, null)}");
            }
        }

        return finalPrice;
    }

    /// <summary>
    /// Gets the price of the item as stated from the beSession handbook values.
    /// </summary>
    public float GetItemHandbookPrice(Item lootItem, BotLog log)
    {
        HandbookData.TryGetValue(lootItem.TemplateId, out var value);
        var price = value?.Price ?? 0f;
        price *= lootItem.StackObjectsCount;

        // if (_log.DebugEnabled)
        // {
        //     if (log != null)
        //     {
        //         log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
        //     }
        //     else
        //     {
        //         _log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
        //     }
        // }

        return Mathf.Max(0f, price);
    }

    /// <summary>
    /// Get the price of a weapon from the sum of its attachments mods, using the ragfair prices to appraise each mod.
    /// </summary>
    public float GetWeaponMarketPrice(Weapon lootWeapon, BotLog log)
    {
        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }
            else
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }
        }

        var finalPrice = 0f;

        // Iterate over each weapon mod and accumulate the price
        foreach (var weaponMod in lootWeapon.Mods)
        {
            finalPrice += GetItemMarketPrice(weaponMod, log);
        }

        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Final price of attachments: {finalPrice} compared to item template {GetItemMarketPrice(lootWeapon, log)}");
            }
            else
            {
                _log.LogDebug($"Final price of attachments: {finalPrice} compared to item template {GetItemMarketPrice(lootWeapon, null)}");
            }
        }

        return finalPrice;
    }

    /// <summary>
    /// Get the price of an armor from the sum of its slots containing armor items, using the ragfair prices to appraise each plate.
    /// </summary>
    public float GetArmorMarketPrice(CompoundItem lootArmor, BotLog log)
    {
        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Getting value of armor {lootArmor.Name.Localized()}");
            }
            else
            {
                _log.LogDebug($"Getting value of armor {lootArmor.Name.Localized()}");
            }
        }

        var finalPrice = 0f;

        // These don't have movable plates, but can have faceshields
        if (lootArmor is Headwear or FaceCover)
        {
            finalPrice += GetItemMarketPrice(lootArmor, log);
        }

        // Iterate over each armor plate and accumulate the price
        foreach (var slot in lootArmor.Slots)
        {
            if (slot.ContainedItem is ArmoredEquipment armoredEquipment)
            {
                finalPrice += GetItemMarketPrice(armoredEquipment, log);
            }
        }

        if (_log.DebugEnabled)
        {
            if (log != null)
            {
                log.LogDebug($"Final price of armor: {finalPrice} compared to item template {GetItemMarketPrice(lootArmor, log)}");
            }
            else
            {
                _log.LogDebug($"Final price of armor: {finalPrice} compared to item template {GetItemMarketPrice(lootArmor, null)}");
            }
        }

        return finalPrice;
    }

    /// <summary>
    /// Gets the price of the item as stated from the ragfair values
    /// </summary>
    public float GetItemMarketPrice(Item lootItem, BotLog log)
    {
        if (MarketData.TryGetValue(lootItem.TemplateId, out var price))
        {
            price *= lootItem.StackObjectsCount;

            // if (_log.DebugEnabled)
            // {
            //     if (log != null)
            //     {
            //         log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
            //     }
            //     else
            //     {
            //         _log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
            //     }
            // }

            return Mathf.Max(0f, price);
        }

        // Fallback
        return GetItemHandbookPrice(lootItem, log);
    }
}
