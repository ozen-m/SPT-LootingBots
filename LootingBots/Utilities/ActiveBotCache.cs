using EFT;

namespace LootingBots.Utilities;

/// <summary>
/// Cached used to keep track of which bots are able to loot
/// </summary>
public static class ActiveBotCache
{
    public static readonly List<BotOwner> ActiveBots = [];

    public static bool IsCacheActive
    {
        get { return LootingBots.MaxActiveLootingBots.Value > 0; }
    }

    public static bool IsAbleToCache
    {
        get { return GetSize() < LootingBots.MaxActiveLootingBots.Value; }
    }

    public static bool IsOverCapacity
    {
        get { return GetSize() > LootingBots.MaxActiveLootingBots.Value; }
    }

    public static void Reset()
    {
        ActiveBots.Clear();
    }

    public static void Add(BotOwner botOwner)
    {
        ActiveBots.Add(botOwner);

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"{botOwner.name.Localized()} looting enabled  (total: {ActiveBots.Count})");
        }
    }

    public static bool Has(BotOwner botOwner)
    {
        return ActiveBots.Contains(botOwner);
    }

    public static void Remove(BotOwner botOwner)
    {
        ActiveBots.Remove(botOwner);

        if (LootingBots.LootLog.DebugEnabled)
        {
            LootingBots.LootLog.LogDebug($"{botOwner.name.Localized()} looting disabled (total: {ActiveBots.Count})");
        }
    }

    public static int GetSize()
    {
        return ActiveBots.Count;
    }
}
