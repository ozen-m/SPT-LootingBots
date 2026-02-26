using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace LootingBots.Components
{
    public class ItemAppraiser
    {
        public readonly Stopwatch LastPriceUpdate = Stopwatch.StartNew();

        public Dictionary<MongoID, HandbookData> HandbookData;
        public Dictionary<MongoID, float> MarketData;

        private Log _log;

        public async Task UpdatePrices()
        {
            _log = LootingBots.ItemAppraiserLog;

            if (LootingBots.UseMarketPrices.Value)
            {
                // ShowMeTheMoney flea prices
                var json = await RequestHandler.GetJsonAsync("/showMeTheMoney/getFleaPrices");
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

                // Initialize ragfair prices from the BE session
                var completionClass = new TaskCompletionSource<Dictionary<MongoID, float>>();
                Singleton<ClientApplication<ISession>>
                    .Instance.GetClientBackEndSession()
                    .RagfairGetPrices(
                        result =>
                        {
                            Dictionary<MongoID, float> prices = null;
                            if (result.Succeed)
                            {
                                prices = result.Value.ToDictionary(
                                    pair => new MongoID(pair.Key),
                                    pair => pair.Value
                                );
                            }

                            completionClass.TrySetResult(prices);
                        }
                    );

                MarketData = await completionClass.Task;
                if (MarketData is null)
                {
                    _log.LogInfo($"Failed to get flea prices from BE session");
                }
            }
            else
            {
                // This is the handbook instance which is initialized when the client first starts.
                HandbookData = Singleton<HandbookClass>.Instance.Items.ToDictionary(item => new MongoID(item.Id));
            }
        }

        /** Will either get the lootItem's price using the ragfair service or the handbook depending on the option selected in the mod menu. If the item is a weapon, will calculate its value based off its attachments if the mod setting is enabled */
        public float GetItemPrice(Item lootItem)
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

            bool valueFromMods = LootingBots.ValueFromMods.Value;
            if (LootingBots.UseMarketPrices.Value && MarketData != null)
            {
                return lootItem is Weapon weapon && valueFromMods ? GetWeaponMarketPrice(weapon) : GetItemMarketPrice(lootItem);
            }

            if (HandbookData != null)
            {
                return lootItem is Weapon weapon && valueFromMods ? GetWeaponHandbookPrice(weapon) : GetItemHandbookPrice(lootItem);
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"ItemAppraiser data is null");
            }

            return 0f;
        }

        /**
        * Get the price of a weapon from the sum of its attachments mods, using the default handbook prices to appraise each mod.
        */
        public float GetWeaponHandbookPrice(Weapon lootWeapon)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }

            float finalPrice = 0f;

            foreach (Mod weaponMod in lootWeapon.Mods)
            {
                finalPrice += GetItemHandbookPrice(weaponMod);
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Final price of attachments: {finalPrice} compared to full item {GetItemHandbookPrice(lootWeapon)}");
            }

            return finalPrice;
        }

        /** Gets the price of the item as stated from the beSession handbook values */
        public float GetItemHandbookPrice(Item lootItem)
        {
            HandbookData.TryGetValue(lootItem.TemplateId, out HandbookData value);
            float price = value?.Price ?? 0f;
            price *= lootItem.StackObjectsCount;

            // if (_log.DebugEnabled)
            // {
            //     _log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
            // }

            return price;
        }

        /**
        * Get the price of a weapon from the sum of its attachments mods, using the ragfair prices to appraise each mod.
        */
        public float GetWeaponMarketPrice(Weapon lootWeapon)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Getting value of attachments for {lootWeapon.Name.Localized()}");
            }

            float finalPrice = 0f;

            // Iterate over each weapon mod and accumulate the price
            foreach (Mod weaponMod in lootWeapon.Mods)
            {
                finalPrice += GetItemMarketPrice(weaponMod);
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Final price of attachments: {finalPrice} compared to item template {GetItemMarketPrice(lootWeapon)}");
            }

            return finalPrice;
        }

        /** Gets the price of the item as stated from the ragfair values */
        public float GetItemMarketPrice(Item lootItem)
        {
            if (MarketData.TryGetValue(lootItem.TemplateId, out var price))
            {
                price *= lootItem.StackObjectsCount;

                // if (_log.DebugEnabled)
                // {
                //     _log.LogDebug($"Price of {lootItem.Name.Localized()} is {price}");
                // }

                return price;
            }

            // Fallback
            return GetItemHandbookPrice(lootItem);
        }
    }
}
