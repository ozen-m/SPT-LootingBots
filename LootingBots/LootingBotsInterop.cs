using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Version = System.Version;

namespace LootingBots;

internal static class LootingBotsInterop
{
    private static readonly Version _requiredVersion = new(1, 8, 0);

    private static bool? _isLootingBotsLoaded;
    private static Func<BotOwner, bool> _forceBotToScanLootMethod;
    private static Func<BotOwner, float, bool> _preventBotFromLootingMethod;
    private static Func<BotOwner, bool> _checkIfInventoryFullMethod;
    private static Func<BotOwner, float> _getNetLootValueMethod;
    private static Func<Item, float> _getItemPriceMethod;

    /// <summary>
    /// Checks if Looting Bots is loaded in the client and initialize the Looting Bots interop class data.
    /// </summary>
    /// <returns>True if Looting Bots is loaded in the client and initialized successfully</returns>
    public static bool Init()
    {
        if (_isLootingBotsLoaded.HasValue)
        {
            return _isLootingBotsLoaded.Value;
        }

        _isLootingBotsLoaded =
            Chainloader.PluginInfos.TryGetValue("me.skwizzy.lootingbots", out var pluginInfo)
            && pluginInfo.Metadata.Version >= _requiredVersion;
        if (_isLootingBotsLoaded.Value)
        {
            var lootingBotsExternalType = Type.GetType("LootingBots.External, Skwizzy.LootingBots");
            var forceBotToScanLootMethod = AccessTools.Method(lootingBotsExternalType, "ForceBotToScanLoot");
            var preventBotFromLootingMethod = AccessTools.Method(lootingBotsExternalType, "PreventBotFromLooting");
            var checkIfInventoryFullMethod = AccessTools.Method(lootingBotsExternalType, "CheckIfInventoryFull");
            var getNetLootValueMethod = AccessTools.Method(lootingBotsExternalType, "GetNetLootValue");
            var getItemPriceMethod = AccessTools.Method(lootingBotsExternalType, "GetItemPrice");
            try
            {
                _forceBotToScanLootMethod = AccessTools.MethodDelegate<Func<BotOwner, bool>>(forceBotToScanLootMethod);
                _preventBotFromLootingMethod = AccessTools.MethodDelegate<Func<BotOwner, float, bool>>(preventBotFromLootingMethod);
                _checkIfInventoryFullMethod = AccessTools.MethodDelegate<Func<BotOwner, bool>>(checkIfInventoryFullMethod);
                _getNetLootValueMethod = AccessTools.MethodDelegate<Func<BotOwner, float>>(getNetLootValueMethod);
                _getItemPriceMethod = AccessTools.MethodDelegate<Func<Item, float>>(getItemPriceMethod);
            }
            catch (Exception)
            {
                // Failed to successfully initialized the interop class
                _isLootingBotsLoaded = false;
            }
        }

        return _isLootingBotsLoaded.Value;
    }

    /// <summary>
    /// Forces a bot to scan for loot as soon as they are able to.
    /// </summary>
    /// <returns>True if successful</returns>
    public static bool TryForceBotToScanLoot(BotOwner botOwner)
    {
        return Init() && _forceBotToScanLootMethod(botOwner);
    }

    /// <summary>
    /// Stops a bot from looting if it is currently looting and prevent loot scans, if Looting Bots is loaded.
    /// </summary>
    /// <param name="duration">The duration, in seconds, to prevent a bot from looting</param>
    /// <returns>True if successful</returns>
    public static bool TryPreventBotFromLooting(BotOwner botOwner, float duration)
    {
        return Init() && _preventBotFromLootingMethod(botOwner, duration);
    }

    /// <summary>
    /// Checks if a bot's inventory is full or not.
    /// </summary>
    /// <returns>True if inventory is full.</returns>
    public static bool CheckIfInventoryFull(BotOwner botOwner)
    {
        return Init() && _checkIfInventoryFullMethod(botOwner);
    }

    /// <summary>
    /// Gets the total value looted by a bot in this raid.
    /// </summary>
    public static float GetNetLootValue(BotOwner botOwner)
    {
        return Init() ? _getNetLootValueMethod(botOwner) : 0f;
    }

    /// <summary>
    /// Checks the price of a loot item using LB ItemAppraiser.
    /// </summary>
    /// <returns>Price of an item. Note: Not per slot pricing.</returns>
    public static float GetItemPrice(Item item)
    {
        return Init() ? _getItemPriceMethod(item) : 0f;
    }
}
