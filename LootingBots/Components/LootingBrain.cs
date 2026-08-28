using System.Diagnostics;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using UnityEngine;

namespace LootingBots.Components;

public class LootingBrain : MonoBehaviour
{
    public BotOwner BotOwner;

    // Component responsible for adding items to the bot inventory
    public LootingInventoryController InventoryController;

    // Current lootable object that the bot will try to loot
    public InteractableObject ActiveLoot;

    // Current lootable object type that the bot will try to loot
    public LootFinder.LootType ActiveLootType = LootFinder.LootType.None;

    // Final destination of the bot when moving to loot something
    public Vector3 Destination;

    // Collider.transform.position for the active lootable. Used in LOS checks to make sure bots dont loot through walls
    public Vector3 LootObjectPosition;

    // ActiveLoot's id for clean up
    public string CachedActiveLootId;

    // Object ids that the bot has looted or failed to reach even though a valid path exists
    public readonly HashSet<string> IgnoredLootIds = [];

    public bool IsPlayerScav;

    // Allows external methods to force the looting brain for a bot to be enabled regardless of performance settings
    public bool ForceBrainEnabled;

    public bool IsBrainEnabled
    {
        get
        {
            return ForceBrainEnabled
                || (
                    !_isDisabledForPerformance
                    && (
                        LootingBots.ContainerLootingEnabled.Value.IsBotEnabled(this)
                        || LootingBots.LooseItemLootingEnabled.Value.IsBotEnabled(this)
                        || LootingBots.CorpseLootingEnabled.Value.IsBotEnabled(this)
                    )
                );
        }
    }

    public BotStats Stats
    {
        get { return InventoryController.Stats; }
    }

    public bool HasActiveLootable
    {
        get { return ActiveLootType is not LootFinder.LootType.None; }
    }

    public bool IsBotLooting
    {
        get { return LootTaskRunning || HasActiveLootable; }
    }

    public bool HasFreeSpace
    {
        get { return Stats.AvailableGridSpaces > LootUtils.RESERVED_SLOT_COUNT; }
    }

    // Boolean showing when the looting coroutine is running
    public bool LootTaskRunning { get; private set; }
    public float DistanceToLoot = float.MaxValue;

    // Delay simulating the time it takes for the UI to open and start searching a container
    public const double LootingStartDelay = 2500D;

    // Interval for the performance check to disable the looting brain
    const float PeformanceTimerInterval = 3f;

    // Max distance from the player a bot can be before their looting brain is disabled
    private double DistanceLimit
    {
        get { return LootingBots.LimitDistanceFromPlayer.Value * LootingBots.LimitDistanceFromPlayer.Value; }
    }

    // Current distance to the player
    private float DistanceToPlayer
    {
        get
        {
            ActiveLootCache.GetClosestPlayer(BotOwner, out var distance);

            return distance;
        }
    }

    // Bot will be considered close enough to the player if the distanceLimit is 0, otherwise the distance from the player must be <= the limit
    private bool IsCloseToPlayer
    {
        get { return DistanceLimit == 0 || DistanceToPlayer <= DistanceLimit; }
    }

    private bool _isDisabledForPerformance;
    private float _performanceTimer;
    private BotLog _log;
    private CancellationTokenSource _lootingCts;
    private Action<Task> _handleExceptionAction;
    private Action _exitPickupStateAction;

    public void Init(BotOwner botOwner)
    {
        _log = new BotLog(LootingBots.LootLog, botOwner);
        _handleExceptionAction = ExceptionHandler;
        _exitPickupStateAction = ExitPickupState;

        BotOwner = botOwner;
        InventoryController = new LootingInventoryController(BotOwner, this);
    }

    /// <summary>
    /// Automatically called as this MonoBehaviour begins running. <br/><br/>
    ///
    /// IMPORTANT: IsPlayerScav MUST be updated after Init() because SPT changes the WildSpawnType for player Scavs after that method is called.
    /// </summary>
    public void Start()
    {
        IsPlayerScav = BotOwner.WillBeAPlayerScav();
        _performanceTimer = Time.time + PeformanceTimerInterval;
        ActiveLootCache.Init();
        ScanScheduler.Init();

        if (ActiveBotCache.IsCacheActive)
        {
            // If there is space in the BotCache, add the bot to the cache. Otherwise disable the looting brain until there is space available in the cache
            if (ForceBrainEnabled || (ActiveBotCache.IsAbleToCache && IsCloseToPlayer))
            {
                ActiveBotCache.Add(BotOwner);
            }
            else
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning(
                        $"Looting disabled! Enabled bots: {ActiveBotCache.GetSize()}. Distance to player: {Math.Sqrt(DistanceToPlayer)}."
                    );
                }

                _isDisabledForPerformance = true;
            }
        }
    }

    /// <summary>
    /// LootFinder update should only be running if one of the looting settings is enabled and the bot is in an active state.
    /// </summary>
    public void Update()
    {
        if (BotOwner.BotState != EBotState.Active)
        {
            return;
        }

        if (ActiveBotCache.IsCacheActive && _performanceTimer < Time.time)
        {
            var closeEnoughToPlayer = IsCloseToPlayer;
            // For a disabled bot to be allowed to loot they must meet the following criteria:
            // 1. The bot has been manually flagged for looting
            //              OR
            // 1. ActiveBotCache is not at capacity
            // 2. Bot is close enough to the player
            if (_isDisabledForPerformance && (ForceBrainEnabled || (ActiveBotCache.IsAbleToCache && closeEnoughToPlayer)))
            {
                ActiveBotCache.Add(BotOwner);
                _isDisabledForPerformance = false;
            }
            // For an enabled bot to become disabled they must meet the following criteria:
            // 1. The bot has not been manually flagged for looting
            // 2. BotCache is over capacity or the bot is no longer close enough to the player
            else if (!ForceBrainEnabled && ActiveBotCache.Has(BotOwner) && (ActiveBotCache.IsOverCapacity || !closeEnoughToPlayer))
            {
                if (IsBotLooting)
                {
                    StopLooting();
                }

                ActiveBotCache.Remove(BotOwner);
                _isDisabledForPerformance = true;

                if (_log.WarningEnabled)
                {
                    _log.LogWarning(
                        $"Looting disabled! Enabled bots: {ActiveBotCache.GetSize()}. Distance to player: {Math.Sqrt(DistanceToPlayer)}."
                    );
                }
            }

            // The performance check should occur every 3 seconds at the minimum.
            // If the loot scan interval is faster, we should do the performance check at the loot scan interval
            _performanceTimer = Time.time + Math.Min(PeformanceTimerInterval, LootingBots.LootScanInterval.Value);
        }

        // This does not work with Fika
        // if (InventoryController.ShouldSort && IsBrainEnabled)
        // {
        //     // Sort items in tacVest for better space management
        //     var tacVest = BotOwner.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem;
        //
        //     _ = InventoryController.SortCompoundItemAsync(tacVest);
        // }
    }

    /// <summary>
    /// Determines the looting action to take depending on the current loot type.
    /// </summary>
    public void StartLooting()
    {
        LootTaskRunning = true;
        _lootTimer.Restart();
        _lootingCts = new CancellationTokenSource(LootingBots.LootTimeout.Value * 1000);

        if (_log.InfoEnabled)
        {
            _log.LogInfo($"Trying to loot {ActiveLoot.GetLootName()} [{ActiveLootType.ToString()}]. Looted: {Stats.Looted:N0}₽");
        }

        switch (ActiveLootType)
        {
            case LootFinder.LootType.Corpse:
                _ = LootCorpseAsync(_lootingCts.Token).ContinueWith(_handleExceptionAction, TaskScheduler.Current);
                break;
            case LootFinder.LootType.Container:
                _ = LootContainerAsync(_lootingCts.Token).ContinueWith(_handleExceptionAction, TaskScheduler.Current);
                break;
            case LootFinder.LootType.Item:
                _ = LootItemAsync(_lootingCts.Token).ContinueWith(_handleExceptionAction, TaskScheduler.Current);
                break;
        }
    }

    /// <summary>
    /// Stops the loot task if running and cleans up loot from the cache
    /// </summary>
    public void StopLooting()
    {
        if (_lootingCts is null)
        {
            CleanupLoot(false);
            return;
        }

        _lootingCts.Cancel();
        _lootingCts.Dispose();
        _lootingCts = null;
    }

    public void OnDestroy()
    {
        StopLooting();
    }

    private readonly Stopwatch _lootTimer = new();
    private readonly List<Item> _itemsToLoot = new(13);

    /// <summary>
    /// Handles looting a corpse found on the map.
    /// </summary>
    private async Task LootCorpseAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            // Initialize corpse inventory equipment
            if (ActiveLoot.GetRootItem() is not InventoryEquipment corpseInventoryEquipment)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"ActiveLoot.Item for Corpse [{ActiveLoot.GetLootName()}] was not InventoryEquipment!");
                }
                return;
            }

            // Get items to loot from the corpse in a priority order based off the slots
            _itemsToLoot.Clear();
            corpseInventoryEquipment.GetPriorityItems(BotOwner.InventoryController.Inventory.Equipment, _itemsToLoot);

            // Do inventory opened animation
            BotOwner.GetPlayer.SetInventoryOpened(true);

            await LootingTransactionController.SimulatePlayerDelayAsync(LootingStartDelay, token);

            InventoryController.SetRootItemOwner(corpseInventoryEquipment.Owner);
            isSuccessful = await InventoryController.TryAddItemsToBotAsync(_itemsToLoot, token);
        }
        finally
        {
            BotOwner.GetPlayer.SetInventoryOpened(false);
            OnLootTaskEnd(isSuccessful);

            if (_log.InfoEnabled)
            {
                _log.LogInfo(
                    $"Corpse loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}"
                );
            }
        }
    }

    /// <summary>
    /// Handles looting a container found on the map.
    /// </summary>
    private async Task LootContainerAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            if (ActiveLoot is not LootableContainer container || container.ItemOwner?.RootItem is not { } item)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning("Tried to loot container but container is empty");
                }
                return;
            }

            // If a container was closed, open it before looting
            var didOpen = false;
            if (container.DoorState == EDoorState.Shut)
            {
                await BotOwner.InteractAsync(container, EInteractionType.Open);
                didOpen = true;
            }

            // Do inventory opened animation
            BotOwner.GetPlayer.SetInventoryOpened(true);

            await LootingTransactionController.SimulatePlayerDelayAsync(LootingStartDelay, token);

            InventoryController.SetRootItemOwner(item.Owner);
            isSuccessful = await InventoryController.LootNestedItemsAsync(item, token);

            // Close the container if the settings to close containers is checked or if the container was already opened when the bot tried to loot it
            if (isSuccessful && (LootingBots.BotsAlwaysCloseContainers.Value || !didOpen))
            {
                await BotOwner.InteractAsync(container, EInteractionType.Close);
            }
        }
        finally
        {
            BotOwner.GetPlayer.SetInventoryOpened(false);
            OnLootTaskEnd(isSuccessful);

            if (_log.InfoEnabled)
            {
                _log.LogInfo(
                    $"Container loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}"
                );
            }
        }
    }

    /// <summary>
    /// Handles looting a loose item found on the map.
    /// </summary>
    public async Task LootItemAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            var item = ActiveLoot.GetRootItem();
            if (item == null)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning("Trying to pick up loose item but is NULL");
                }
                return;
            }

            // Set _isInPatrol to true, this tricks SAIN to not set patrol.
            // This lets us play the pick-up animation.
            BotOwner.GetPlayer.MovementContext._isInPatrol = true;

            _itemsToLoot.Clear();
            _itemsToLoot.Add(item);
            InventoryController.SetRootItemOwner(item.Owner);
            isSuccessful = await InventoryController.TryAddItemsToBotAsync(_itemsToLoot, token);
            if (isSuccessful)
            {
                // Do pick up animation if we successfully looted the item
                BotOwner.GetPlayer.CurrentManagedState.Pickup(true, _exitPickupStateAction);
            }
        }
        finally
        {
            BotOwner.GetPlayer.MovementContext._isInPatrol = false;
            OnLootTaskEnd(isSuccessful);

            if (_log.InfoEnabled)
            {
                _log.LogInfo(
                    $"Loose item loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}"
                );
            }
        }
    }

    public void OnLootTaskEnd(bool lootingSuccessful)
    {
        _lootTimer.Stop();

        // Only ignore if looting was successful.
        CleanupLoot(lootingSuccessful);

        InventoryController.UpdateActiveWeapon();
        InventoryController.UpdateGridStats();
        InventoryController.SetRootItemOwner(null);
        BotOwner.AIData.CalcPower();
        LootTaskRunning = false;
    }

    public void UpdateGridStats()
    {
        InventoryController.UpdateGridStats();
    }

    /// <summary>
    /// Check to see if the object being looted has been ignored due to bad navigation, or looted already.
    ///
    /// (1.7.x) No longer checks if its in use by another bot, since it can conflict with prioritized loot.
    /// </summary>
    public bool IsLootIgnored(string lootId)
    {
        return lootId == null || IgnoredLootIds.Contains(lootId);
    }

    /// <summary>
    /// Check if the item being looted meets the loot value threshold specified in the mod settings.
    /// PMC bots use the PMC loot threshold, all other bots such as scavs, bosses, and raiders will use the scav threshold.
    /// </summary>
    public bool IsValuableEnough(Item lootItem)
    {
        var itemValue = LootingBots.ItemAppraiser.GetItemPrice(lootItem, _log);
        return InventoryController.IsValuableEnough(
            itemValue / lootItem.GetItemSize() /* Divide by slots to get price per slot */
        );
    }

    /// <summary>
    /// Adds a loot id to the list of loot items to ignore for a specific bot
    /// </summary>
    public void IgnoreLoot(string id)
    {
        IgnoredLootIds.Add(id);
    }

    /// <summary>
    /// Cleans the ActiveLoot from the active loot cache.
    /// By default, also adds the ActiveLoot to the bot's ignore list for the LootFinder to ignore.
    /// </summary>
    /// <param name="ignore">If true, adds the active loot to the bot's ignore list</param>
    public void CleanupLoot(bool ignore = true)
    {
        if (ActiveLootType == LootFinder.LootType.None)
        {
            // Nothing to clean up
            return;
        }

        if (ignore)
        {
            IgnoreLoot(CachedActiveLootId);
        }
        else if (ActiveLoot is Corpse corpse && BotOwner.GetPlayer != null && BotOwner.GetPlayer.TryGetComponent(out LootFinder lootFinder))
        {
            lootFinder.EnqueuePriorityCorpse(corpse.PlayerProfileID);
        }

        ActiveLootCache.Cleanup(CachedActiveLootId);
        SetLoot(null, LootFinder.LootType.None, Vector3.zero, Vector3.zero, string.Empty);
    }

    public void SetLoot(
        InteractableObject interactableObject,
        LootFinder.LootType lootType,
        Vector3 position,
        Vector3 destination,
        string lootId,
        float dist = float.MaxValue
    )
    {
        ActiveLoot = interactableObject;
        ActiveLootType = lootType;
        LootObjectPosition = position;
        Destination = destination;
        CachedActiveLootId = lootId;
        DistanceToLoot = dist != float.MaxValue ? dist * dist : dist;
    }

    private void ExitPickupState()
    {
        var player = BotOwner.GetPlayer;
        player.UpdateInteractionCast();
        if (player.CurrentState is PickUpState pickupState)
        {
            pickupState.Pickup(false, null);
        }
    }

    private void ExceptionHandler(Task task)
    {
        if (task.IsCanceled)
        {
            if (_lootTimer.ElapsedMilliseconds / 1000L > LootingBots.LootTimeout.Value)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"Looting interrupted due to timeout ({LootingBots.LootTimeout.Value}s)");
                }
            }
            else if (_log.DebugEnabled)
            {
                _log.LogDebug("Looting interrupted");
            }
            return;
        }

        if (task.IsFaulted)
        {
            _log.LogError("Exception while trying to loot:");
            _log.LogError(task.Exception!.ToString());
        }
    }
}
