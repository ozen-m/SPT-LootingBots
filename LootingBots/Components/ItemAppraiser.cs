using System.Diagnostics;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace LootingBots.Components;

public class ItemAppraiser(Log _log)
{
    public readonly Stopwatch LastPriceUpdate = Stopwatch.StartNew();

    public Dictionary<MongoID, HandbookData> HandbookData;
    public Dictionary<MongoID, float> MarketData;

    public bool IsUpdatingPrices { get; private set; }

    public async UniTask UpdatePricesAsync()
    {
        IsUpdatingPrices = true;

        try
        {
            if (LootingBots.UseMarketPrices.Value)
            {
                // ShowMeTheMoney flea prices
                var json = await RequestHandler.GetJsonAsync("/showMeTheMoney/getFleaPrices").AsUniTask();
                if (!json.IsNullOrEmpty())
                {
                    try
                    {
                        MarketData = JsonConvert.DeserializeObject<Dictionary<MongoID, float>>(json);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
                if (MarketData is not null)
                {
                    _log.LogInfo("ShowMeTheMoney flea prices successfully fetched!");
                    LastPriceUpdate.Restart();
                    return;
                }

                _log.LogInfo("ShowMeTheMoney flea prices not available, falling back to BE session");

                var completionClass = new UniTaskCompletionSourceEx<Result<Dictionary<string, float>>>();
                Singleton<ClientApplication<ISession>>.Instance.GetClientBackEndSession().RagfairGetPrices(completionClass.SetResult);

                Dictionary<MongoID, float> prices = null;
                var ragfairPrices = await completionClass.Task;
                if (ragfairPrices.Succeed)
                {
                    prices = ragfairPrices.Value.ToDictionary(pair => new MongoID(pair.Key), pair => pair.Value);
                }

                MarketData = prices;
                if (MarketData is null)
                {
                    _log.LogInfo("Failed to get flea prices from BE session");
                }
            }
            else
            {
                // This is the handbook instance which is initialized when the client first starts.
                HandbookData = Singleton<HandbookClass>.Instance.Items.ToDictionary(item => new MongoID(item.Id));
            }
        }
        finally
        {
            IsUpdatingPrices = false;
        }
    }

    /// <summary>
    /// Will either get the lootItem's price using the ragfair service or the handbook depending on the option selected in the mod menu.
    /// If the item is a weapon, will calculate its value based off its attachments if the mod setting is enabled.
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

        var valueFromMods = LootingBots.ValueFromMods.Value;
        if (LootingBots.UseMarketPrices.Value && MarketData != null)
        {
            return lootItem is Weapon weapon && valueFromMods ? GetWeaponMarketPrice(weapon, log) : GetItemMarketPrice(lootItem, log);
        }

        if (HandbookData != null)
        {
            return lootItem is Weapon weapon && valueFromMods ? GetWeaponHandbookPrice(weapon, log) : GetItemHandbookPrice(lootItem, log);
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
