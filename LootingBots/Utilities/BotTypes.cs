using System.Runtime.CompilerServices;
using EFT;
using LootingBots.Components;

namespace LootingBots.Utilities;

[Flags]
public enum BotType
{
    Scav = 1,
    Pmc = 2,
    PlayerScav = 4,
    Raider = 8,
    Cultist = 16,
    Boss = 32,
    Follower = 64,
    Bloodhound = 128,

    None = 0,
    All = Scav | Pmc | PlayerScav | Raider | Cultist | Boss | Follower | Bloodhound,
}

public static class BotTypeUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasScav(this BotType botType)
    {
        return (botType & BotType.Scav) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasPmc(this BotType botType)
    {
        return (botType & BotType.Pmc) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasPlayerScav(this BotType botType)
    {
        return (botType & BotType.PlayerScav) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasRaider(this BotType botType)
    {
        return (botType & BotType.Raider) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasCultist(this BotType botType)
    {
        return (botType & BotType.Cultist) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasBoss(this BotType botType)
    {
        return (botType & BotType.Boss) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFollower(this BotType botType)
    {
        return (botType & BotType.Follower) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasBloodhound(this BotType botType)
    {
        return (botType & BotType.Bloodhound) != 0;
    }

    public static bool IsBotEnabled(this BotType enabledTypes, LootingBrain brain)
    {
        return brain.IsPlayerScav ? enabledTypes.HasPlayerScav() : enabledTypes.IsBotEnabled(brain.BotOwner.Profile.Info.Settings.Role);
    }

    public static bool IsBotEnabled(this BotType enabledTypes, WildSpawnType botType)
    {
        switch (botType)
        {
            case WildSpawnType.pmcBEAR:
            case WildSpawnType.pmcUSEC:
            {
                return enabledTypes.HasPmc();
            }
            case WildSpawnType.bossBully:
            case WildSpawnType.bossGluhar:
            case WildSpawnType.bossKilla:
            case WildSpawnType.bossKnight:
            case WildSpawnType.bossKojaniy:
            case WildSpawnType.bossSanitar:
            case WildSpawnType.bossTagilla:
            case WildSpawnType.bossTest:
            case WildSpawnType.bossZryachiy:
            case WildSpawnType.bossBoar:
            case WildSpawnType.bossKolontay:
            case WildSpawnType.bossPartisan:
            case WildSpawnType.bossTagillaAgro:
            case WildSpawnType.bossKillaAgro:
            {
                return enabledTypes.HasBoss();
            }
            case WildSpawnType.assault:
            case WildSpawnType.assaultGroup:
            {
                return enabledTypes.HasScav();
            }
            case WildSpawnType.followerBigPipe:
            case WildSpawnType.followerBirdEye:
            case WildSpawnType.followerBully:
            case WildSpawnType.followerGluharAssault:
            case WildSpawnType.followerGluharScout:
            case WildSpawnType.followerGluharSecurity:
            case WildSpawnType.followerGluharSnipe:
            case WildSpawnType.followerKojaniy:
            case WildSpawnType.followerSanitar:
            case WildSpawnType.followerTagilla:
            case WildSpawnType.followerTest:
            case WildSpawnType.followerZryachiy:
            case WildSpawnType.followerKolontayAssault:
            case WildSpawnType.followerKolontaySecurity:
            case WildSpawnType.bossBoarSniper:
            case WildSpawnType.followerBoarClose1:
            case WildSpawnType.followerBoarClose2:
            case WildSpawnType.followerBoar:
            case WildSpawnType.tagillaHelperAgro:
            {
                return enabledTypes.HasFollower();
            }
            case WildSpawnType.exUsec:
            case WildSpawnType.pmcBot:
            {
                return enabledTypes.HasRaider();
            }
            case WildSpawnType.sectantPriest:
            case WildSpawnType.sectantWarrior:
            case WildSpawnType.cursedAssault:
            case WildSpawnType.sectantPredvestnik:
            case WildSpawnType.sectantPrizrak:
            case WildSpawnType.sectantOni:
            {
                return enabledTypes.HasCultist();
            }
            case WildSpawnType.arenaFighter:
            case WildSpawnType.arenaFighterEvent:
            case WildSpawnType.crazyAssaultEvent:
            {
                return enabledTypes.HasBloodhound();
            }
            default:
                return false;
        }
    }

    public static bool IsPMC(this WildSpawnType wildSpawnType)
    {
        return wildSpawnType is WildSpawnType.pmcBEAR or WildSpawnType.pmcUSEC;
    }

    public static bool IsScav(this WildSpawnType wildSpawnType)
    {
        return wildSpawnType is WildSpawnType.assault or WildSpawnType.assaultGroup;
    }

    public static bool IsBoss(this WildSpawnType wildSpawnType)
    {
        return _bossWildTypes.Contains(wildSpawnType);
    }

    /// <summary>
    /// Determines if the bot with the given profile will be a player Scav
    /// </summary>
    public static bool WillBeAPlayerScav(this IPlayer iPlayer)
    {
        // Handle the old version of creating player Scavs
        var profileInfo = iPlayer.Profile.Info;
        if (profileInfo.Nickname.Contains(" ("))
        {
            return true;
        }

        // Check for player Scavs created by SPT
        return profileInfo.Settings.Role == WildSpawnType.assault && !string.IsNullOrEmpty(profileInfo.MainProfileNickname);
    }

    private static readonly HashSet<WildSpawnType> _bossWildTypes =
    [
        WildSpawnType.bossBully,
        WildSpawnType.bossGluhar,
        WildSpawnType.bossKilla,
        WildSpawnType.bossKnight,
        WildSpawnType.bossKojaniy,
        WildSpawnType.bossSanitar,
        WildSpawnType.bossTagilla,
        WildSpawnType.bossTest,
        WildSpawnType.bossZryachiy,
        WildSpawnType.bossBoar,
        WildSpawnType.bossKolontay,
        WildSpawnType.bossPartisan,
        WildSpawnType.bossTagillaAgro,
        WildSpawnType.bossKillaAgro,
    ];
}
