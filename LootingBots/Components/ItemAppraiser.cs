using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using LootingBots.Utilities.Extensions;
using UnityEngine;

namespace LootingBots.Components;

public class ItemAppraiser(Log _log)
{
    private const float PriceUpdateInterval = 1800f; // 30 minutes
    public float NextPriceUpdate = -1f;

    public Dictionary<MongoID, float> HandbookData;
    public Dictionary<MongoID, float> MarketData;

    public bool IsUpdatingPrices { get; private set; }

    public async Task UpdatePricesAsync(CancellationToken token = default)
    {
        IsUpdatingPrices = true;
        try
        {
            if (LootingBots.UseMarketPrices.Value)
            {
                var tcs = new TaskCompletionSource<Result<Dictionary<string, float>>>();
                using var registration = token.Register(
                    static tcs => ((TaskCompletionSource<Result<Dictionary<string, float>>>)tcs).SetCanceled(),
                    tcs
                );
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
                HandbookData = Singleton<Handbook>.Instance.Items.ToDictionary(item => new MongoID(item.Id), item => item.Price);
                if (HandbookData is null)
                {
                    _log.LogError("Failed to get handbook data");
                }
            }
        }
        catch (Exception e)
        {
            _log.LogError(e.ToString());
            _log.LogError("Failed to get item prices");
        }
        finally
        {
            NextPriceUpdate = Time.time + PriceUpdateInterval;
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
            var ammo = box.Cartridges.Last;
            if (ammo is not null)
            {
                lootItem = ammo;
            }
        }

        if (LootingBots.UseMarketPrices.Value && MarketData is not null)
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

        if (HandbookData is not null)
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
            if (log is not null)
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
            if (log is not null)
            {
                log.LogDebug($"Getting value of attachments for {lootWeapon.LocalizedName()}");
            }
            else
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.LocalizedName()}");
            }
        }

        var finalPrice = 0f;

        // Iterate over each weapon mod and accumulate the price
        using var pooledList = UnityEngine.Pool.ListPool<Mod>.Get(out var mods);
        lootWeapon.GetAllSlotContainedItems(mods);
        foreach (var mod in mods)
        {
            finalPrice += GetItemHandbookPrice(mod, log);
        }
        finalPrice *= GetQualityModifier(lootWeapon);

        if (_log.DebugEnabled)
        {
            if (log is not null)
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
            if (log is not null)
            {
                log.LogDebug($"Getting value of armor {lootArmor.LocalizedName()}");
            }
            else
            {
                _log.LogDebug($"Getting value of armor {lootArmor.LocalizedName()}");
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
            if (log is not null)
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
        if (HandbookData.TryGetValue(lootItem.TemplateId, out var price))
        {
            price *= GetQualityModifier(lootItem);
            price *= lootItem.StackObjectsCount;
        }

        // if (_log.DebugEnabled)
        // {
        //     if (log is not null)
        //     {
        //         log.LogDebug($"Handbook price of {lootItem.LocalizedName()}: {price:N0}₽");
        //     }
        //     else
        //     {
        //         _log.LogDebug($"Handbook price of {lootItem.LocalizedName()}: {price:N0}₽");
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
            if (log is not null)
            {
                log.LogDebug($"Getting value of attachments for {lootWeapon.LocalizedName()}");
            }
            else
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.LocalizedName()}");
            }
        }

        var finalPrice = 0f;

        // Iterate over each weapon mod and accumulate the price
        using var pooledList = UnityEngine.Pool.ListPool<Mod>.Get(out var mods);
        lootWeapon.GetAllSlotContainedItems(mods);
        foreach (var mod in mods)
        {
            finalPrice += GetItemMarketPrice(mod, log);
        }
        finalPrice *= GetQualityModifier(lootWeapon);

        if (_log.DebugEnabled)
        {
            if (log is not null)
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
            if (log is not null)
            {
                log.LogDebug($"Getting value of armor {lootArmor.LocalizedName()}");
            }
            else
            {
                _log.LogDebug($"Getting value of armor {lootArmor.LocalizedName()}");
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
            if (log is not null)
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
            price *= GetQualityModifier(lootItem);
            price *= lootItem.StackObjectsCount;

            // if (_log.DebugEnabled)
            // {
            //     if (log is not null)
            //     {
            //         log.LogDebug($"Market price of {lootItem.LocalizedName()}: {price:N0}₽");
            //     }
            //     else
            //     {
            //         _log.LogDebug($"Market price of {lootItem.LocalizedName()}: {price:N0}₽");
            //     }
            // }

            return Mathf.Max(0f, price);
        }

        // Fallback
        return GetItemHandbookPrice(lootItem, log);
    }

    private static float GetQualityModifier(Item item)
    {
        var modifier = 1f;
        foreach (var component in item.Components)
        {
            switch (component)
            {
                case ResourceComponent resource:
                    if (resource.Value > 0f)
                    {
                        modifier *= resource.Value / resource.MaxResource;
                    }
                    break;
                case MedKitComponent medKit:
                    modifier *= medKit.HpResource / medKit.MaxHpResource;
                    break;
                case FoodDrinkComponent foodDrink:
                    modifier *= foodDrink.HpPercent / foodDrink.MaxResource;
                    break;
                case RepairableComponent repairable:
                    var durability = repairable.Durability / repairable.TemplateDurability;
                    modifier *= durability != 0f ? Mathf.Sqrt(durability) : 1f;
                    break;
                case KeyComponent key:
                    if (key.NumberOfUsages > 0 && key.Template.MaximumNumberOfUsage > 0)
                    {
                        var maximumNumberOfUsage = key.Template.MaximumNumberOfUsage;
                        modifier *= (maximumNumberOfUsage - key.NumberOfUsages) / (float)maximumNumberOfUsage;
                    }
                    break;
                case RepairKitComponent repairKit:
                    modifier *= repairKit.Resource / repairKit._template.MaxRepairResource;
                    break;
            }
        }

        return modifier;
    }
}
