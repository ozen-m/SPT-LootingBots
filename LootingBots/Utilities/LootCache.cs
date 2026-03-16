using Comfort.Common;
using EFT;

namespace LootingBots.Utilities;

/// <summary>
/// Tracks lootable objects currently targeted by bots to prevent multiple bots
/// from navigating to the same lootable simultaneously.
/// </summary>
public static class ActiveLootCache
{
    // Handle to the players instance for use in friendly checks
    public static List<IPlayer> ActivePlayers { get; } = [];

    public static Dictionary<string, BotOwner> ActiveLoot { get; } = [];

    public static void Init()
    {
        if (ActivePlayers.Count > 0)
        {
            return;
        }

        foreach (var player in Singleton<GameWorld>.Instance.RegisteredPlayers)
        {
            if (player.IsAI)
            {
                continue;
            }

            if (!player.HealthController.IsAlive)
            {
                continue;
            }

            ActivePlayers.Add(player);
        }
    }

    public static void Reset()
    {
        ActiveLoot.Clear();
        ActivePlayers.Clear();
    }

    public static bool CacheActiveLootId(string containerId, BotOwner botOwner)
    {
        return !string.IsNullOrEmpty(botOwner.name) && !string.IsNullOrEmpty(containerId) && ActiveLoot.TryAdd(containerId, botOwner);
    }

    public static bool IsLootInUse(string lootId)
    {
        return ActiveLoot.ContainsKey(lootId);
    }

    private static readonly List<string> _keysToRemoveScratch = [];

    public static void Cleanup(BotOwner botOwner)
    {
        try
        {
            // Check to make sure the BotOwner we are cleaning up has a valid name
            if (botOwner == null || botOwner.name == null)
            {
                if (LootingBots.LootLog.ErrorEnabled)
                {
                    LootingBots.LootLog.LogError("Cleanup issued on a bot with no name?");
                }
                return;
            }

            _keysToRemoveScratch.Clear();

            // Look through the entries in the dictionary and remove any that match the specified bot owner
            foreach (var keyValue in ActiveLoot)
            {
                // Check to make sure the BotOwner saved in the dictionary has a valid name before comparing
                if (keyValue.Value == null || keyValue.Value.name == null)
                {
                    if (LootingBots.LootLog.ErrorEnabled)
                    {
                        LootingBots.LootLog.LogError("Bot in loot cache has no name?");
                    }

                    // Bot is null, so remove it from active loot cache
                    _keysToRemoveScratch.Add(keyValue.Key);
                    continue;
                }

                // If the bot's name matches, remove the item
                if (keyValue.Value.name == botOwner.name)
                {
                    _keysToRemoveScratch.Add(keyValue.Key);
                }
            }

            foreach (var key in _keysToRemoveScratch)
            {
                ActiveLoot.Remove(key);
            }
        }
        catch (Exception e)
        {
            LootingBots.LootLog.LogError(e);
        }
    }
}
