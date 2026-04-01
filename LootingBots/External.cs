using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Components;
using LootingBots.Utilities;

namespace LootingBots;

public static class External
{
    private static readonly ConditionalWeakTable<BotOwner, BotLog> _interopLogs = [];

    /// <summary>
    /// Forces a bot to scan for loot as soon as they are able to.
    /// </summary>
    public static bool ForceBotToScanLoot(BotOwner bot)
    {
        if (GetAllComponents(bot, out var lootingBrain, out var lootFinder))
        {
            var log = GetOrCreateInteropLog(bot);

            if (!lootingBrain.HasFreeSpace)
            {
                if (log.WarningEnabled)
                {
                    log.LogWarning("Forcing a scan but bot does not have enough free space");
                }
            }
            else if (log.DebugEnabled)
            {
                log.LogDebug("Forcing a loot scan");
            }

            lootFinder.ForceScan();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Stops a bot from looting if it is currently looting something and prevent loot scans.
    /// </summary>
    /// <param name="duration">The duration, in seconds, to prevent a bot from looting</param>
    public static bool PreventBotFromLooting(BotOwner bot, float duration)
    {
        if (GetAllComponents(bot, out var lootingBrain, out var lootFinder))
        {
            var log = GetOrCreateInteropLog(bot);

            if (log.DebugEnabled)
            {
                log.LogDebug($"Preventing a bot from looting for the next {duration} seconds");
            }

            if (lootingBrain.IsBrainEnabled)
            {
                lootFinder.OverrideNextScanTime(duration);

                lootingBrain.StopLooting();
            }

            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a bot's inventory is full or not.
    /// </summary>
    public static bool CheckIfInventoryFull(BotOwner bot)
    {
        if (GetLootingBrain(bot, out var lootingBrain))
        {
            var log = GetOrCreateInteropLog(bot);

            if (log.DebugEnabled)
            {
                log.LogDebug($"Checking if {bot.name} has Free Space in their inventory. Result: {lootingBrain.HasFreeSpace}");
            }

            return !lootingBrain.HasFreeSpace;
        }
        return false;
    }

    /// <summary>
    /// Gets the total value looted by a bot in this raid.
    /// </summary>
    public static float GetNetLootValue(BotOwner bot)
    {
        if (GetLootingBrain(bot, out var lootingBrain))
        {
            var log = GetOrCreateInteropLog(bot);

            if (log.DebugEnabled)
            {
                log.LogDebug($"Getting Looted Value for {bot.name} which is {lootingBrain.Stats.Looted:N0}");
            }

            return lootingBrain.Stats.Looted;
        }
        return 0f;
    }

    /// <summary>
    /// Checks the price of a loot item using LB ItemAppraiser.
    /// Note: Not per slot pricing.
    /// </summary>
    public static float GetItemPrice(Item item)
    {
        return LootingBots.ItemAppraiser != null ? LootingBots.ItemAppraiser.GetItemPrice(item, null) : 0;
    }

    private static bool GetAllComponents(BotOwner bot, out LootingBrain lootingBrain, out LootFinder lootFinder)
    {
        var hasLootFinder = GetLootFinder(bot, out lootFinder);
        var hasLootingBrain = GetLootingBrain(bot, out lootingBrain);
        return hasLootingBrain && hasLootFinder;
    }

    private static bool GetLootingBrain(BotOwner bot, out LootingBrain lootingBrain)
    {
        lootingBrain = bot.GetPlayer.gameObject.GetComponent<LootingBrain>();
        return lootingBrain != null;
    }

    private static bool GetLootFinder(BotOwner bot, out LootFinder lootFinder)
    {
        lootFinder = bot.GetPlayer.gameObject.GetComponent<LootFinder>();
        return lootFinder != null;
    }

    private static BotLog GetOrCreateInteropLog(BotOwner bot)
    {
        if (_interopLogs.TryGetValue(bot, out var log))
        {
            return log;
        }

        log = new BotLog(LootingBots.InteropLog, bot);
        _interopLogs.Add(bot, log);
        return log;
    }
}
