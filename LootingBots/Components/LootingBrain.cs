using System.Diagnostics;
using Cysharp.Threading.Tasks;
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
    public Vector3 Destination = Vector3.zero;

    // Collider.transform.position for the active lootable. Used in LOS checks to make sure bots dont loot through walls
    public Vector3 LootObjectPosition;

    // Object ids that the bot has looted
    public HashSet<string> IgnoredLootIds;

    // Object ids that were not able to be reached even though a valid path exists. Is cleared every 2 mins by default
    public HashSet<string> NonNavigableLootIds;

    public bool IsPlayerScav;

    public bool LockUntilNextScan;

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
        get { return ActiveLootType is not LootFinder.LootType.None && ActiveLoot != null; }
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
    public float DistanceToLoot = -1f;

    // Delay simulating the time it takes for the UI to open and start searching a container
    public const double LootingStartDelay = 2500D;

    // Interval for the performance check to disable the looting brain
    const float PeformanceTimerInterval = 3f;

    // Max distance from the player a bot can be before their looting brain is disabled
    private double DistanceLimit
    {
        get { return Math.Pow(LootingBots.LimitDistanceFromPlayer.Value, 2); }
    }

    // Current distance to the player
    private float DistanceToPlayer
    {
        get
        {
            IPlayer closestPlayer = ActiveLootCache.ActivePlayers.GetClosestPlayer(BotOwner);

            if (closestPlayer == null)
            {
                return float.MaxValue;
            }

            return (BotOwner.Position - closestPlayer.Position).sqrMagnitude;
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

    public void Init(BotOwner botOwner)
    {
        _log = new BotLog(LootingBots.LootLog, botOwner);
        BotOwner = botOwner;
        InventoryController = new LootingInventoryController(BotOwner, this);
        IgnoredLootIds = [];
        NonNavigableLootIds = [];
    }

    /*
     * Automatically called as this MonoBehaviour begins running.
     *
     * IMPORTANT: IsPlayerScav MUST be updated after Init() because SPT changes the WildSpawnType for player Scavs after that method is called.
     */
    public void Start()
    {
        IsPlayerScav = BotOwner.Profile.WillBeAPlayerScav();
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

    /*
     * LootFinder update should only be running if one of the looting settings is enabled and the bot is in an active state
     */
    public void Update()
    {
        try
        {
            if (BotOwner.BotState == EBotState.Active)
            {
                if (ActiveBotCache.IsCacheActive && _performanceTimer < Time.time)
                {
                    bool closeEnoughToPlayer = IsCloseToPlayer;
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
                    // 1. Bot is not currently trying to loot something
                    // 2. BotCache is over capacity or the bot is no longer close enough to the player
                    else if (
                        !HasActiveLootable
                        && !ForceBrainEnabled
                        && ActiveBotCache.Has(BotOwner)
                        && (ActiveBotCache.IsOverCapacity || !closeEnoughToPlayer)
                    )
                    {
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

                if (IsBrainEnabled)
                {
                    // Does not work in Fika
                    // if (InventoryController.ShouldSort)
                    // {
                    //     // Sort items in tacVest for better space management
                    //     SearchableItemItemClass tacVest = (SearchableItemItemClass)
                    //         BotOwner.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
                    //
                    //     StartCoroutine(InventoryController.SortSearchableItem(tacVest));
                    // }

                    // Open any nearby door
                    BotOwner.DoorOpener.UpdateDoorInteractionStatus();

                    // If a player picks up an item that was marked as active by a bot, its ItemOwner?.RootItem will be null. In this case cleanup the active item
                    if (ActiveLoot == null)
                    {
                        return;
                    }

                    switch (ActiveLoot)
                    {
                        case LootableContainer container when container.ItemOwner?.RootItem != null:
                        case LootItem lootItem when lootItem.ItemOwner?.RootItem != null:
                            return;
                        default:
                            CleanupLoot(false);
                            break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            _log.LogError(e);
        }
    }

    /**
    * Determines the looting action to take depending on the current Active object in the LootFinder. There can only be one Active object at a time
    */
    public void StartLooting()
    {
        StopLooting();

        LootTaskRunning = true;
        _lootingCts = new CancellationTokenSource(LootingBots.LootTimeout.Value * 1000);

        if (_log.InfoEnabled)
        {
            _log.LogInfo($"Trying to loot {ActiveLoot.GetLootName()} [{ActiveLootType.ToString()}]. Looted: {Stats.Looted:N0}₽");
        }

        switch (ActiveLootType)
        {
            case LootFinder.LootType.Corpse:
                LootCorpseAsync(_lootingCts.Token).Forget(ExceptionHandler);
                break;
            case LootFinder.LootType.Container:
                LootContainerAsync(_lootingCts.Token).Forget(ExceptionHandler);
                break;
            case LootFinder.LootType.Item:
                LootItemAsync(_lootingCts.Token).Forget(ExceptionHandler);
                break;
        }
    }

    public void StopLooting()
    {
        if (_lootingCts is null)
        {
            return;
        }

        _lootingCts.Cancel();
        _lootingCts.Dispose();
        _lootingCts = null;
    }

    private readonly Stopwatch _lootTimer = new();
    private readonly List<Item> _itemsToLoot = new(13);

    /**
    * Handles looting a corpse found on the map.
    */
    private async UniTask LootCorpseAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            _lootTimer.Restart();

            // Initialize corpse inventory equipment
            if (ActiveLoot.GetRootItem() is not InventoryEquipment corpseInventoryEquipment)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"ActiveLoot.Item for Corpse [{ActiveLoot.GetLootName()}] was not InventoryEquipment!");
                }
                return;
            }

            // Get items to loot from the corpse in a priority order based off the slots
            _itemsToLoot.Clear();
            corpseInventoryEquipment.GetPriorityItems(BotOwner.InventoryController.Inventory.Equipment, _itemsToLoot);

            await LootingTransactionController.SimulatePlayerDelayAsync(LootingStartDelay, token);

            isSuccessful = await InventoryController.TryAddItemsToBotAsync(_itemsToLoot, token);

            await UniTask.WaitWhile(BotOwner.InventoryController, static invCont => invCont.IsChangingWeapon, cancellationToken: token);
        }
        finally
        {
            InventoryController.UpdateActiveWeapon();

            // Only ignore the corpse if looting was not interrupted
            CleanupLoot(isSuccessful);
            OnLootTaskEnd(isSuccessful);

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Corpse loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}");
            }
        }
    }

    /**
    * Handles looting a container found on the map.
    */
    private async UniTask LootContainerAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            _lootTimer.Restart();

            if (ActiveLoot is not LootableContainer container || container.ItemOwner?.RootItem is not { } item)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning("Tried to loot container but container is empty");
                }
                return;
            }

            // If a container was closed, open it before looting
            bool didOpen = false;
            if (container.DoorState == EDoorState.Shut)
            {
                LootUtils.InteractContainer(container, BotOwner, EInteractionType.Open, _log);
                didOpen = true;
            }

            await LootingTransactionController.SimulatePlayerDelayAsync(LootingStartDelay, token);

            isSuccessful = await InventoryController.LootNestedItemsAsync((SearchableItemItemClass) item, token);

            // Close the container if the settings to close containers is checked or if the container was already opened when the bot tried to loot it
            if (isSuccessful && (LootingBots.BotsAlwaysCloseContainers.Value || !didOpen))
            {
                LootUtils.InteractContainer(container, BotOwner, EInteractionType.Close, _log);
            }

            await UniTask.WaitWhile(BotOwner.InventoryController, static invCont => invCont.IsChangingWeapon, cancellationToken: token);
        }
        finally
        {
            InventoryController.UpdateActiveWeapon();

            // Only ignore the container if looting was not interrupted
            CleanupLoot(isSuccessful);
            OnLootTaskEnd(isSuccessful);

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Container loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}");
            }
        }
    }

    /**
    * Handles looting a loose item found on the map.
    */
    public async UniTask LootItemAsync(CancellationToken token)
    {
        var isSuccessful = false;
        try
        {
            _lootTimer.Restart();

            var item = ActiveLoot.GetRootItem();
            if (item == null)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning("Trying to pick up loose item but is NULL");
                }
                return;
            }

            _itemsToLoot.Clear();
            _itemsToLoot.Add(item);
            isSuccessful = await InventoryController.TryAddItemsToBotAsync(_itemsToLoot, token);

            await UniTask.WaitWhile(BotOwner.InventoryController, static invCont => invCont.IsChangingWeapon, cancellationToken: token);

        }
        finally
        {
            InventoryController.UpdateActiveWeapon();

            // Need to manually cleanup item because the ItemOwner on the original object changes. Only ignore if looting was not interrupted
            CleanupLoot(isSuccessful);
            OnLootTaskEnd(isSuccessful);

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Loose item loot time: {_lootTimer.ElapsedMilliseconds / 1000f:F0}s. Looted: {Stats.Looted:N0}₽. Was successful: {isSuccessful}");
            }
        }
    }

    public void OnLootTaskEnd(bool lootingSuccessful)
    {
        _lootTimer.Stop();

        Destination = Vector3.zero;
        UpdateGridStats();
        BotOwner.AIData.CalcPower();
        LootTaskRunning = false;
    }

    public void UpdateGridStats()
    {
        InventoryController.UpdateGridStats();
    }

    /**
    *  Check to see if the object being looted has been ignored due to bad navigation, being looted already, or if its in use by another bot
    */
    public bool IsLootIgnored(string lootId)
    {
        bool alreadyTried = NonNavigableLootIds.Contains(lootId) || IgnoredLootIds.Contains(lootId);

        return lootId == null || alreadyTried || ActiveLootCache.IsLootInUse(lootId);
    }

    /** Check if the item being looted meets the loot value threshold specified in the mod settings. PMC bots use the PMC loot threshold, all other bots such as scavs, bosses, and raiders will use the scav threshold */
    public bool IsValuableEnough(Item lootItem)
    {
        float itemValue = LootingBots.ItemAppraiser.GetItemPrice(lootItem, _log);
        return InventoryController.IsValuableEnough(itemValue / lootItem.GetItemSize() /* Divide by slots to get price per slot */);
    }

    /**
    *  Handles adding non navigable loot to the list of non-navigable ids for use in the ignore logic. Additionaly removes the object from the active loot cache
    */
    public void HandleNonNavigableLoot()
    {
        string lootId = ActiveLoot.GetRootItemId();

        if (lootId != null)
        {
            NonNavigableLootIds.Add(lootId);
        }

        Cleanup();
    }

    /**
    * Adds a loot id to the list of loot items to ignore for a specific bot
    */
    public void IgnoreLoot(string id)
    {
        IgnoredLootIds.Add(id);
    }

    /**
    * Removes all active lootables from LootFinder and cleans them from the active loot cache
    */
    public void Cleanup(bool ignore = true)
    {
        if (ActiveLoot != null)
        {
            CleanupLoot(ignore);
        }
    }

    /**
    * Removes the ActiveItem from the LootFinder and ActiveLootCache. Can optionally add the item to the ignore list after cleaning
    */
    public void CleanupLoot(bool ignore = true)
    {
        var item = ActiveLoot.GetRootItem();
        if (item != null)
        {
            if (ignore)
            {
                IgnoreLoot(item.Id);
            }
        }

        ActiveLootCache.Cleanup(BotOwner);
        SetLoot(null, LootFinder.LootType.None);
    }

    public void SetLoot(InteractableObject loot, LootFinder.LootType type)
    {
        ActiveLoot = loot;
        ActiveLootType = type;
    }

    private void ExceptionHandler(Exception ex)
    {
        if (ex is OperationCanceledException)
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
        if (_log.ErrorEnabled)
        {
            _log.LogError("Exception while trying to loot:");
            _log.LogError(ex.ToString());
        }
    }
}
