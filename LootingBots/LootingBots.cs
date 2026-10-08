using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HandBook;
using LootingBots.Components;
using LootingBots.Utilities;
using SPT.Reflection.Patching;
using UnityEngine;

namespace LootingBots;

[BepInPlugin(MOD_GUID, MOD_NAME, MOD_VERSION)]
[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
[BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("com.chazut.orbit")]
public class LootingBots : BaseUnityPlugin
{
    private PatchManager _patchManager;

    private const string MOD_GUID = "me.skwizzy.lootingbots";
    private const string MOD_NAME = "LootingBots";
    private const string MOD_VERSION = "1.8.0";

    public const BotType SettingsDefaults = BotType.Scav | BotType.Pmc | BotType.PlayerScav | BotType.Raider;

    public const EquipmentType CanPickupEquipmentDefaults =
        EquipmentType.ArmoredRig
        | EquipmentType.Chest
        | EquipmentType.Backpack
        | EquipmentType.Grenade
        | EquipmentType.Helmet
        | EquipmentType.TacticalRig
        | EquipmentType.Weapon
        | EquipmentType.Dogtag
        | EquipmentType.Earpiece
        | EquipmentType.FaceCover
        | EquipmentType.Eyewear
        | EquipmentType.Armband;

    public const LogLevel DefaultLogLevel = LogLevel.Error | LogLevel.Warning;

    // Loot Finder Settings
    public static ConfigEntry<BotType> ContainerLootingEnabled;
    public static ConfigEntry<BotType> DetectContainerNeedsSight;
    public static ConfigEntry<float> DetectContainerDistance;

    public static ConfigEntry<BotType> CorpseLootingEnabled;
    public static ConfigEntry<BotType> DetectCorpseNeedsSight;
    public static ConfigEntry<float> DetectCorpseDistance;

    public static ConfigEntry<BotType> LooseItemLootingEnabled;
    public static ConfigEntry<BotType> DetectItemNeedsSight;
    public static ConfigEntry<float> DetectItemDistance;

    // Loot Finder Settings (Timing)
    public static ConfigEntry<float> InitialStartTimer;
    public static ConfigEntry<float> LootScanInterval;
    public static ConfigEntry<int> NoLootCooldown;

    // Loot Settings
    public static ConfigEntry<bool> BotsAlwaysCloseContainers;
    public static ConfigEntry<bool> UseMarketPrices;
    public static ConfigEntry<bool> ValueFromMods;
    public static ConfigEntry<bool> ValueFromPlates;
    public static ConfigEntry<bool> CanStripAttachments;
    public static ConfigEntry<bool> AllowContainerNesting;
    public static ConfigEntry<bool> UseWeightRestriction;

    public static ConfigEntry<float> PMCMinLootThreshold;
    public static ConfigEntry<float> PMCMaxLootThreshold;
    public static ConfigEntry<CanEquipEquipmentType> PMCGearToEquip;
    public static ConfigEntry<EquipmentType> PMCGearToPickup;

    public static ConfigEntry<float> ScavMinLootThreshold;
    public static ConfigEntry<float> ScavMaxLootThreshold;
    public static ConfigEntry<CanEquipEquipmentType> ScavGearToEquip;
    public static ConfigEntry<EquipmentType> ScavGearToPickup;

    // Loot Settings (Timing)
    public static ConfigEntry<bool> UseSearchTime;
    public static ConfigEntry<double> TransactionDelay;
    public static ConfigEntry<int> LootTimeout;

    // Performance Settings
    public static ConfigEntry<int> MaxActiveLootingBots;
    public static ConfigEntry<int> LimitDistanceFromPlayer;
    public static ConfigEntry<int> MaxConcurrentScans;

    // Debug Settings
    public static ConfigEntry<LogLevel> LootingLogLevels;
    public static ConfigEntry<LogLevel> InteropLogLevels;
    public static ConfigEntry<LogLevel> ItemAppraiserLogLevels;
    public static ConfigEntry<int> FilterLogsOnBot;
    public static ConfigEntry<bool> DebugLootNavigation;

    public static ItemAppraiser ItemAppraiser { get; private set; }
    public static Log LootLog;
    public static Log InteropLog;
    public static Log ItemAppraiserLog;

    public void LootFinderSettings()
    {
        CorpseLootingEnabled = Config.Bind(
            "Loot Finder",
            "Enable corpse looting",
            SettingsDefaults,
            new ConfigDescription(
                "Enables corpse looting for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 10 }
            )
        );
        DetectCorpseNeedsSight = Config.Bind(
            "Loot Finder",
            "Enable corpse line of sight check",
            BotType.None,
            new ConfigDescription(
                "When scanning for loot, corpses will be ignored if they are not visible for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 9 }
            )
        );
        DetectCorpseDistance = Config.Bind(
            "Loot Finder",
            "Detect corpse distance",
            80f,
            new ConfigDescription(
                "Distance (in meters) a bot is able to detect a corpse",
                null,
                new ConfigurationManagerAttributes { Order = 8 }
            )
        );

        ContainerLootingEnabled = Config.Bind(
            "Loot Finder",
            "Enable container looting",
            SettingsDefaults,
            new ConfigDescription(
                "Enables container looting for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 7 }
            )
        );
        DetectContainerNeedsSight = Config.Bind(
            "Loot Finder",
            "Enable container line of sight check",
            BotType.None,
            new ConfigDescription(
                "When scanning for loot, containers will be ignored if they are not visible for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 6 }
            )
        );
        DetectContainerDistance = Config.Bind(
            "Loot Finder",
            "Detect container distance",
            80f,
            new ConfigDescription(
                "Distance (in meters) a bot is able to detect a container",
                null,
                new ConfigurationManagerAttributes { Order = 5 }
            )
        );

        LooseItemLootingEnabled = Config.Bind(
            "Loot Finder",
            "Enable loose item looting",
            SettingsDefaults,
            new ConfigDescription(
                "Enables loose item looting for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 4 }
            )
        );
        DetectItemNeedsSight = Config.Bind(
            "Loot Finder",
            "Enable item line of sight check",
            BotType.None,
            new ConfigDescription(
                "When scanning for loot, loose items will be ignored if they are not visible for the selected bot types",
                null,
                new ConfigurationManagerAttributes { Order = 3 }
            )
        );
        DetectItemDistance = Config.Bind(
            "Loot Finder",
            "Detect item distance",
            80f,
            new ConfigDescription(
                "Distance (in meters) a bot is able to detect an item",
                null,
                new ConfigurationManagerAttributes { Order = 2 }
            )
        );

        // Loot Finder (Timing)
        InitialStartTimer = Config.Bind(
            "Loot Finder (Timing)",
            "Delay after spawn",
            6f,
            new ConfigDescription(
                "The amount of seconds a bot will wait before starting their first loot scan after spawning into raid",
                null,
                new ConfigurationManagerAttributes { Order = 3 }
            )
        );
        LootScanInterval = Config.Bind(
            "Loot Finder (Timing)",
            "Loot scan interval",
            5f,
            new ConfigDescription(
                "The amount of seconds a bot will wait until triggering another loot scan",
                null,
                new ConfigurationManagerAttributes { Order = 2 }
            )
        );
        NoLootCooldown = Config.Bind(
            "Loot Finder (Timing)",
            "No loot cooldown",
            180,
            new ConfigDescription(
                "The amount of seconds a bot will wait until triggering another loot scan when a previous loot scan comes up empty",
                null,
                new ConfigurationManagerAttributes { Order = 1 }
            )
        );
    }

    public void LootSettings()
    {
        BotsAlwaysCloseContainers = Config.Bind(
            "Loot Settings",
            "Bots always close containers",
            true,
            new ConfigDescription(
                "When enabled, bots will always try to close a container after they have finished looting. If the bot is interrupted while looting, the container may remain open.",
                null,
                new ConfigurationManagerAttributes { Order = 14 }
            )
        );
        UseMarketPrices = Config.Bind(
            "Loot Settings",
            "Use flea market prices",
            false,
            new ConfigDescription(
                "Bots will query more accurate ragfair prices to do item value checks. Will make a query to get ragfair prices when the client is first started.",
                null,
                new ConfigurationManagerAttributes { Order = 13 }
            )
        );
        ValueFromMods = Config.Bind(
            "Loot Settings",
            "Calculate weapon value from attachments",
            true,
            new ConfigDescription(
                "Calculate weapon value by looking up each attachment. More accurate than just looking at the base weapon template but a slightly more expensive check.",
                null,
                new ConfigurationManagerAttributes { Order = 12 }
            )
        );
        ValueFromPlates = Config.Bind(
            "Loot Settings",
            "Calculate armor value from slotted items",
            true,
            new ConfigDescription(
                "Calculate armor value by looking up each slot containing plates/faceshields etc. More accurate than just looking at the base armor template but a slightly more expensive check.",
                null,
                new ConfigurationManagerAttributes { Order = 11 }
            )
        );
        CanStripAttachments = Config.Bind(
            "Loot Settings",
            "Allow weapon attachment stripping",
            true,
            new ConfigDescription(
                "Allows bots to take the attachments off of a weapon if they cannot pick it up, or if they discard a previously owned weapon",
                null,
                new ConfigurationManagerAttributes { Order = 10 }
            )
        );
        AllowContainerNesting = Config.Bind(
            "Loot Settings",
            "Allow container nesting",
            true,
            new ConfigDescription(
                "Allows bots to nest containers such as backpacks and vests. Slightly more expensive to calculate.",
                null,
                new ConfigurationManagerAttributes { Order = 9 }
            )
        );
        UseWeightRestriction = Config.Bind(
            "Loot Settings",
            "Use weight restriction",
            false,
            new ConfigDescription(
                "Disallow bots from picking up an item if the item's weight will exceed their overweight limit. Takes effect next raid.", // Technically next bot spawn
                null,
                new ConfigurationManagerAttributes { Order = 8 }
            )
        );

        PMCMinLootThreshold = Config.Bind(
            "Loot Settings",
            "PMC: Min loot value threshold",
            15000f,
            new ConfigDescription(
                "PMC bots will only loot items that exceed the specified value in roubles. When set to 0, bots will ignore the minimum value threshold.",
                null,
                new ConfigurationManagerAttributes { Order = 7 }
            )
        );
        PMCMaxLootThreshold = Config.Bind(
            "Loot Settings",
            "PMC: Max loot value threshold",
            0f,
            new ConfigDescription(
                "PMC bots will NOT loot items that exceed the specified value in roubles. When set to 0, bots will ignore the maximum value threshold.",
                null,
                new ConfigurationManagerAttributes { Order = 6 }
            )
        );
        PMCGearToEquip = Config.Bind(
            "Loot Settings",
            "PMC: Allowed gear to equip",
            CanEquipEquipmentType.All,
            new ConfigDescription(
                "The equipment a PMC bot is able to equip during raid",
                null,
                new ConfigurationManagerAttributes { Order = 5 }
            )
        );
        PMCGearToPickup = Config.Bind(
            "Loot Settings",
            "PMC: Allowed gear in bags",
            CanPickupEquipmentDefaults,
            new ConfigDescription(
                "The equipment a PMC bot is able to place in their backpack/rig",
                null,
                new ConfigurationManagerAttributes { Order = 4 }
            )
        );

        ScavMinLootThreshold = Config.Bind(
            "Loot Settings",
            "Scav: Min loot value threshold",
            5000f,
            new ConfigDescription(
                "All non-PMC bots will only loot items that exceed the specified value in roubles. When set to 0, bots will ignore the minimum value threshold.",
                null,
                new ConfigurationManagerAttributes { Order = 3 }
            )
        );
        ScavMaxLootThreshold = Config.Bind(
            "Loot Settings",
            "Scav: Max loot value threshold",
            0f,
            new ConfigDescription(
                "All non-PMC bots will NOT loot items that exceed the specified value in roubles. When set to 0, bots will ignore the maximum value threshold.",
                null,
                new ConfigurationManagerAttributes { Order = 2 }
            )
        );
        ScavGearToEquip = Config.Bind(
            "Loot Settings",
            "Scav: Allowed gear to equip",
            CanEquipEquipmentType.All,
            new ConfigDescription(
                "The equipment a non-PMC bot is able to equip during raid",
                null,
                new ConfigurationManagerAttributes { Order = 1 }
            )
        );
        ScavGearToPickup = Config.Bind(
            "Loot Settings",
            "Scav: Allowed gear in bags",
            CanPickupEquipmentDefaults,
            new ConfigDescription(
                "The equipment a non-PMC bot is able to place in their backpack/rig",
                null,
                new ConfigurationManagerAttributes { Order = 0 }
            )
        );

        UseSearchTime = Config.Bind(
            "Loot Settings (Timing)",
            "Enable search time",
            true,
            new ConfigDescription(
                "Adds a delay before looting an item to simulate the time it takes for a bot to \"search\" an item in a searchable container. The delay is calculated using the AttentionLootSpeed and SearchBuffSpeed skills of the bot.",
                null,
                new ConfigurationManagerAttributes { Order = 3 }
            )
        );
        TransactionDelay = Config.Bind(
            "Loot Settings (Timing)",
            "Delay after taking item (ms)",
            3000D,
            new ConfigDescription(
                "The amount of milliseconds a bot will wait after taking an item into their inventory before attempting to loot another item. Simulates the amount of time it takes for a player to look through loot decide to take something.",
                null,
                new ConfigurationManagerAttributes { Order = 2 }
            )
        );
        LootTimeout = Config.Bind(
            "Loot Settings (Timing)",
            "Loot Timeout",
            300,
            new ConfigDescription(
                "Time in seconds before a looting bot is timed out and stops looting",
                null,
                new ConfigurationManagerAttributes { Order = 1 }
            )
        );
    }

    public void PerformanceSettings()
    {
        MaxActiveLootingBots = Config.Bind(
            "Performance",
            "Maximum looting bots",
            20,
            new ConfigDescription(
                "Limits the amount of bots that are able to simultaneously run looting logic. A value of 0 represents no limit.",
                null,
                new ConfigurationManagerAttributes { Order = 3 }
            )
        );
        LimitDistanceFromPlayer = Config.Bind(
            "Performance",
            "Limit looting by distance to player",
            0,
            new ConfigDescription(
                "Any bot farther than the specified distance in meters will not run any looting logic. A value of 0 represents no limit.",
                null,
                new ConfigurationManagerAttributes { Order = 2 }
            )
        );
        MaxConcurrentScans = Config.Bind(
            "Performance",
            "Maximum concurrent scans",
            3,
            new ConfigDescription(
                "Max number of bots that can scan for loot at the same time. A value of 0 represents no limit. Takes effect next raid.",
                new AcceptableValueRange<int>(0, 35),
                new ConfigurationManagerAttributes { Order = 1 }
            )
        );
    }

    public void DebugSettings()
    {
        LootingLogLevels = Config.Bind(
            "Debug",
            "Log Levels",
            DefaultLogLevel,
            new ConfigDescription(
                "Enable different levels of log messages to show in the logs",
                null,
                new ConfigurationManagerAttributes { Order = 5, IsAdvanced = true }
            )
        );
        InteropLogLevels = Config.Bind(
            "Debug",
            "Interop Log Levels",
            DefaultLogLevel,
            new ConfigDescription(
                "Enable different levels of log messages specific to the mod interop methods",
                null,
                new ConfigurationManagerAttributes { Order = 4, IsAdvanced = true }
            )
        );
        ItemAppraiserLogLevels = Config.Bind(
            "Debug",
            "Item Appraiser Log Levels",
            DefaultLogLevel,
            new ConfigDescription(
                "Enables logs for the item appraiser that calculates the weapon values",
                null,
                new ConfigurationManagerAttributes { Order = 3, IsAdvanced = true }
            )
        );
        FilterLogsOnBot = Config.Bind(
            "Debug",
            "Filter logs on bot",
            0,
            new ConfigDescription(
                "Filters new log entries only showing logs for the number of the bot specified. A value of 0 denotes no filter.",
                null,
                new ConfigurationManagerAttributes { Order = 2, IsAdvanced = true }
            )
        );
        DebugLootNavigation = Config.Bind(
            "Debug",
            "Show navigation points",
            false,
            new ConfigDescription(
                "Renders spheres where bots are trying to navigate when container looting.\n(Red): Container position\n(Green): NavMesh corrected container position\n(Blue): Calculated bot destination (where the bot will move to).",
                null,
                new ConfigurationManagerAttributes { Order = 1, IsAdvanced = true }
            )
        );
    }

    public void Awake()
    {
        _patchManager = new PatchManager(this, true);

        LootFinderSettings();
        LootSettings();
        PerformanceSettings();
        DebugSettings();
        Config.SettingChanged += OnSettingsChanged;

        LootLog = new Log(Logger, LootingLogLevels);
        InteropLog = new Log(Logger, InteropLogLevels);
        ItemAppraiserLog = new Log(Logger, ItemAppraiserLogLevels);
        ItemAppraiser = new ItemAppraiser(ItemAppraiserLog);

        _patchManager.EnablePatches();

        BrainManager.RemoveLayer(
            "Utility peace",
            ["Assault", "ExUsec", "BossSanitar", "CursAssault", "PMC", "PmcUsec", "PmcBear", "ExUsec", "ArenaFighter", "SectantWarrior"]
        );

        // Remove BSG's own looting layer
        BrainManager.RemoveLayer("LootPatrol", ["Assault", "PmcUsec", "PmcBear"]);

        BrainManager.AddCustomLayer(
            typeof(LootingLayer),
            [
                "Assault",
                "CursAssault",
                "BossSanitar",
                "BossKojaniy",
                "BossGluhar",
                "BossPartisan",
                "BossKolontay",
                "BirdEye",
                "BigPipe",
                "Knight",
                "Tagilla",
                "Killa",
                "BossSanitar",
                "BossBully",
                "BossBoar",
                "FollowerGluharScout",
                "FollowerGluharProtect",
                "FollowerGluharAssault",
                "Fl_Zraychiy",
                "TagillaFollower",
                "KolonSec",
                "FollowerSanitar",
                "FollowerBully",
                "FlBoar",
            ],
            4
        );

        BrainManager.AddCustomLayer(typeof(LootingLayer), ["PMC", "PmcUsec", "PmcBear", "ExUsec", "ArenaFighter"], 5);

        BrainManager.AddCustomLayer(typeof(LootingLayer), ["SectantWarrior"], 13);

        BrainManager.AddCustomLayer(typeof(LootingLayer), ["SectantPriest"], 13);

        BrainManager.AddCustomLayer(typeof(LootingLayer), ["Obdolbs"], 11);

        FikaHandler.Init();
    }

    public void Update()
    {
        if (ItemAppraiser.IsUpdatingPrices)
        {
            return;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (InGameStatus.InRaid)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            return;
        }

        if (UseMarketPrices.Value)
        {
            if (ItemAppraiser.NextPriceUpdate > Time.time && ItemAppraiser.MarketData is not null)
            {
                return;
            }
        }
        else
        {
            if (ItemAppraiser.HandbookData is not null)
            {
                return;
            }
        }

        if (Singleton<Handbook>.Instance is null || Singleton<ClientApplication<IEftSession>>.Instance == null)
        {
            return;
        }

        ItemAppraiserLog.LogInfo("Updating item appraiser");
        _ = ItemAppraiser.UpdatePricesAsync(destroyCancellationToken);
    }

    /// <summary>
    /// Since these settings are initialized on bot spawn, update their values for each bot when in-raid.
    /// </summary>
    private static void OnSettingsChanged(object obj, SettingChangedEventArgs args)
    {
        var gameWorld = Singleton<GameWorld>.Instance;
        if (gameWorld == null)
        {
            return;
        }

        if (
            args.ChangedSetting != ContainerLootingEnabled
            && args.ChangedSetting != LooseItemLootingEnabled
            && args.ChangedSetting != CorpseLootingEnabled
            && args.ChangedSetting != DetectCorpseNeedsSight
            && args.ChangedSetting != DetectContainerNeedsSight
            && args.ChangedSetting != DetectItemNeedsSight
        )
        {
            return;
        }

        foreach (var player in gameWorld.AllAlivePlayersList)
        {
            if (!player.IsAI)
            {
                continue;
            }

            if (player.TryGetComponent(out LootingBrain lootingBrain))
            {
                lootingBrain.UpdateIsLootingEnabled();
            }
            if (player.TryGetComponent(out LootFinder lootFinder))
            {
                lootFinder.UpdateFinderSettings();
            }
        }
    }
}
