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
    private static readonly List<IPlayer> _activePlayers = [];

    private static readonly Dictionary<string, BotOwner> _activeLoot = [];

    public static void Init()
    {
        if (_activePlayers.Count > 0)
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

            _activePlayers.Add(player);
        }
    }

    public static void Reset()
    {
        _activeLoot.Clear();
        _activePlayers.Clear();
    }

    public static bool CacheActiveLootId(string lootId, BotOwner botOwner)
    {
        return botOwner != null && !string.IsNullOrEmpty(lootId) && _activeLoot.TryAdd(lootId, botOwner);
    }

    public static bool IsLootInUse(string lootId)
    {
        return _activeLoot.ContainsKey(lootId);
    }

    public static void Cleanup(string lootId)
    {
        if (string.IsNullOrEmpty(lootId))
        {
            return;
        }

        if (_activeLoot.Remove(lootId, out _))
        {
            return;
        }

        if (LootingBots.LootLog.WarningEnabled)
        {
            LootingBots.LootLog.LogWarning($"Could not find loot id to remove from ActiveLootCache: {lootId}");
        }
    }

    public static IPlayer GetClosestPlayer(BotOwner botOwner, out float closestDistance)
    {
        closestDistance = float.MaxValue;

        if (_activePlayers.Count == 0)
        {
            return null;
        }

        IPlayer closestPlayer = null;
        foreach (var player in _activePlayers)
        {
            if (!player.HealthController.IsAlive)
            {
                continue;
            }

            var distance = (botOwner.Position - player.Position).sqrMagnitude;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }
}
