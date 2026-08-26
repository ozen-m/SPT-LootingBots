using EFT;

namespace LootingBots.Utilities;

/// <summary>
/// Cached used to keep track of which bots are able to loot
/// </summary>
public static class ActiveBotCache
{
    private static readonly HashSet<BotOwner> _activeBots = [];

    public static bool IsCacheActive
    {
        get { return LootingBots.MaxActiveLootingBots.Value > 0; }
    }

    public static bool IsAbleToCache
    {
        get { return _activeBots.Count < LootingBots.MaxActiveLootingBots.Value; }
    }

    public static bool IsOverCapacity
    {
        get { return _activeBots.Count > LootingBots.MaxActiveLootingBots.Value; }
    }

    public static void Reset()
    {
        _activeBots.Clear();
    }

    public static void Add(BotOwner botOwner)
    {
        _activeBots.Add(botOwner);

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"{botOwner.name.Localized()} looting enabled (total: {_activeBots.Count})");
        }
    }

    public static bool Has(BotOwner botOwner)
    {
        return _activeBots.Contains(botOwner);
    }

    public static void Remove(BotOwner botOwner)
    {
        _activeBots.Remove(botOwner);

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"{botOwner.name.Localized()} looting disabled (total: {_activeBots.Count})");
        }
    }

    public static int GetSize()
    {
        return _activeBots.Count;
    }
}
