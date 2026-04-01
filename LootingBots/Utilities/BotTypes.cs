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
    public static bool HasScav(this BotType botType)
    {
        return (botType & BotType.Scav) != 0;
    }

    public static bool HasPmc(this BotType botType)
    {
        return (botType & BotType.Pmc) != 0;
    }

    public static bool HasPlayerScav(this BotType botType)
    {
        return (botType & BotType.PlayerScav) != 0;
    }

    public static bool HasRaider(this BotType botType)
    {
        return (botType & BotType.Raider) != 0;
    }

    public static bool HasCultist(this BotType botType)
    {
        return (botType & BotType.Cultist) != 0;
    }

    public static bool HasBoss(this BotType botType)
    {
        return (botType & BotType.Boss) != 0;
    }

    public static bool HasFollower(this BotType botType)
    {
        return (botType & BotType.Follower) != 0;
    }

    public static bool HasBloodhound(this BotType botType)
    {
        return (botType & BotType.Bloodhound) != 0;
    }

    public static bool IsBotEnabled(this BotType enabledTypes, LootingBrain brain)
    {
        if (brain.IsPlayerScav)
        {
            return enabledTypes.HasPlayerScav();
        }
        var role = brain.BotOwner.Profile.Info.Settings.Role;
        return enabledTypes.IsBotEnabled(role);
    }

    public static bool IsBotEnabled(this BotType enabledTypes, WildSpawnType botType)
    {
        if (botType.IsPMC())
        {
            return enabledTypes.HasPmc();
        }

        if (IsBoss(botType))
        {
            return enabledTypes.HasBoss();
        }

        switch (botType)
        {
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

    public static bool IsBoss(WildSpawnType wildSpawnType)
    {
        var bosses = new List<WildSpawnType>
        {
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
        };
        return bosses.Contains(wildSpawnType);
    }

    /// <summary>
    /// Determines if the bot with the given profile will be a player Scav
    /// </summary>
    public static bool WillBeAPlayerScav(this Profile profile)
    {
        // Handle the old version of creating player Scavs
        if (profile.Info.Nickname.Contains(" ("))
        {
            return true;
        }

        // Check for player Scavs created by SPT
        return profile.Info.Settings.Role == WildSpawnType.assault && !string.IsNullOrEmpty(profile.Info.MainProfileNickname);
    }
}
