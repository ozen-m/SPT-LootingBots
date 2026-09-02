using Comfort.Common;
using EFT;
using UnityEngine.Pool;

namespace LootingBots.Utilities;

/// <summary>
/// Tracks lootable objects currently targeted by bots to prevent multiple friendly bots
/// from navigating to the same lootable simultaneously.
/// </summary>
public static class ActiveLootCache
{
    /// <summary>
    /// Handle to the players instance for use in distance checks
    /// </summary>
    private static readonly List<IPlayer> _activePlayers = [];

    /// <summary>
    /// Handle to the looters(BotOwner) of loot id
    /// </summary>
    private static readonly Dictionary<string, HashSet<BotOwner>> _activeLoot = [];

    /// <summary>
    /// Initialize the bot players list for distance checks
    /// </summary>
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
        foreach (var (_, looters) in _activeLoot)
        {
            looters.Clear();
            HashSetPool<BotOwner>.Release(looters);
        }
        _activeLoot.Clear();
        _activePlayers.Clear();
    }

    public static bool CacheActiveLootId(string lootId, BotOwner botOwner)
    {
        if (botOwner == null || string.IsNullOrEmpty(lootId))
        {
            return false;
        }

        if (!_activeLoot.TryGetValue(lootId, out var looters))
        {
            looters = HashSetPool<BotOwner>.Get();
            _activeLoot.Add(lootId, looters);
        }
        return looters.Add(botOwner);
    }

    public static bool IsLootInUse(string lootId, BotOwner botOwner)
    {
        if (_activeLoot.TryGetValue(lootId, out var looters))
        {
            foreach (var looter in looters)
            {
                if (!botOwner.BotsGroup.IsPlayerEnemy(looter))
                {
                    return true; // botOwner is friendly to looter, disallow looting the same loot
                }
            }
            return false; // No friendlies looting
        }

        return false; // lootId is not being looted
    }

    public static void Cleanup(string lootId, BotOwner botOwner)
    {
        if (botOwner == null || string.IsNullOrEmpty(lootId))
        {
            return;
        }

        if (_activeLoot.TryGetValue(lootId, out var looters))
        {
            if (!looters.Remove(botOwner))
            {
                if (LootingBots.LootLog.WarningEnabled)
                {
                    LootingBots.LootLog.LogWarning($"Could not find bot owner to remove from ActiveLootCache: {lootId}");
                }
            }
            if (looters.Count == 0)
            {
                _activeLoot.Remove(lootId);
                HashSetPool<BotOwner>.Release(looters);
            }
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
