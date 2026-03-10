using System.Buffers;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using LootingBots.Utilities;
using UnityEngine;
using UnityEngine.AI;

namespace LootingBots.Components;

public class LootFinder : MonoBehaviour
{
    private static readonly ArrayPool<Collider> _colliderPool = ArrayPool<Collider>.Shared;

    private LootingBrain _lootingBrain;
    private BotOwner _botOwner;
    private BotLog _log;

    private float _scanTimer;
    private bool _lockUntilNextScan;

    private const int MaxEmptyAttempts = 3;
    private const float EmptyAttemptsCooldown = 180f;
    private int _emptyAttempts;
    // TODO: Add config

    public bool IsScheduledScan
    {
        get { return _scanTimer < Time.time; }
    }

    private static float DetectCorpseDistance
    {
        get { return LootingBots.DetectCorpseDistance.Value; }
    }
    private static float DetectContainerDistance
    {
        get { return LootingBots.DetectContainerDistance.Value; }
    }
    private static float DetectItemDistance
    {
        get { return LootingBots.DetectItemDistance.Value; }
    }

    public enum LootType : byte
    {
        None = 0,
        Corpse = 1,
        Container = 2,
        Item = 3,
    }

    public bool IsScanRunning { get; private set; }
    private CancellationTokenSource _lootFinderCts;

    public void Init(BotOwner botOwner)
    {
        _scanTimer = Time.time + LootingBots.InitialStartTimer.Value;
        _botOwner = botOwner;
        _lootingBrain = _botOwner.GetPlayer.gameObject.GetComponent<LootingBrain>();
        _log = new BotLog(LootingBots.LootLog, _botOwner);
    }

    public void ResetScanTimer()
    {
        // If the loot finder is locked, do not reset it
        if (!_lockUntilNextScan)
        {
            _scanTimer = Time.time + LootingBots.LootScanInterval.Value;
        }
    }

    public void BeginSearch(int ticket)
    {
        IsScanRunning = true;

        StopFindLootTask();
        _lootFinderCts = new CancellationTokenSource();
        FindLootAsync(ticket, _lootFinderCts.Token).Forget(ExceptionHandler);

        SetLockUntilNextScan(false);
    }

    public void ForceScan()
    {
        _scanTimer = Time.time - 1f;
        SetLockUntilNextScan(true);
        _lootingBrain.ForceBrainEnabled = true;
    }

    public void OverrideNextScanTime(float scanTime)
    {
        _scanTimer = Time.time + scanTime;
        SetLockUntilNextScan(true);
    }

    public void SetLockUntilNextScan(bool value)
    {
        _lockUntilNextScan = value;
    }

    public void StopFindLootTask()
    {
        if (_lootFinderCts is null)
        {
            return;
        }

        _lootFinderCts.Cancel();
        _lootFinderCts.Dispose();
        _lootFinderCts = null;
    }

    private async UniTask FindLootAsync(int queue, CancellationToken token)
    {
        IsScanRunning = true;

        Collider[] colliders = _colliderPool.Rent(3000);

        try
        {
            if (_botOwner == null)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug("BotOwner is NULL, cannot start scan!");
                }
                return;
            }

            // Use the largest detection radius specified in the settings as the main Sphere radius
            float detectionRadius = Mathf.Max(DetectItemDistance, DetectContainerDistance);
            detectionRadius = Mathf.Max(detectionRadius, DetectCorpseDistance);
            var botPosition = _botOwner.Position;

            // Cast a sphere on the bot, detecting any Interactive world objects that collide with the sphere
            var hits = Physics.OverlapSphereNonAlloc(
                _botOwner.Position,
                detectionRadius,
                colliders,
                LootUtils.LootMask,
                QueryTriggerInteraction.Ignore
            );

            await UniTask.Yield(token);

            if (hits == 0)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug("No loot in range");
                }
                return;
            }

            // Sort colliders by distance
            Array.Sort(colliders, 0, hits, new ColliderDistanceComparer(botPosition));

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Scan results: {hits}");
            }

            await UniTask.Yield(token);

            int rangeCalculations = 0;
            const int maxRangeCalculations = 30;

            // Cache these values to avoid repeated property access
            var containerLootingEnabled = LootingBots.ContainerLootingEnabled.Value.IsBotEnabled(_lootingBrain);
            var itemLootingEnabled = LootingBots.LooseItemLootingEnabled.Value.IsBotEnabled(_lootingBrain);
            var corpseLootingEnabled = LootingBots.CorpseLootingEnabled.Value.IsBotEnabled(_lootingBrain);
            var availableGridSpaces = _lootingBrain.Stats.AvailableGridSpaces;

            // Process sorted colliders
            for (int i = 0; i < hits; i++)
            {
                token.ThrowIfCancellationRequested();

                var collider = colliders[i];

                Item rootItem = null;
                LootType lootType = LootType.None;

                // Get InteractableObject once and check derived type
                var interactableObject = collider.gameObject.GetComponentInParent<InteractableObject>();
                if (corpseLootingEnabled && interactableObject is Corpse corpse)
                {
                    var player = collider.gameObject.GetComponentInParent<Player>();
                    if (player != null && // Corpse is a bot corpse and not a static "Dead scav"
                        corpse.ItemOwner?.RootItem is InventoryEquipment equipment)
                    {
                        rootItem = equipment;
                        lootType = LootType.Corpse;
                    }
                }
                else if (containerLootingEnabled && interactableObject is LootableContainer container)
                {
                    rootItem = container.ItemOwner?.RootItem;
                    if (container.isActiveAndEnabled // Container is marked as active and enabled
                        && container.DoorState is not EDoorState.Locked) // Container is not locked)
                    {
                        lootType = LootType.Container;
                    }
                }
                else if (itemLootingEnabled && interactableObject is LootItem lootItem && lootItem is not Corpse)
                {
                    rootItem = lootItem.ItemOwner?.RootItem;
                    if (rootItem is not null
                        && !rootItem.QuestItem // Item is not a quest item
                        && (
                            rootItem is SearchableItemItemClass // If the item is something that can be searched, consider it lootable
                            || (
                                rootItem is ArmoredEquipmentItemClass armor
                                && _lootingBrain.InventoryController.IsBetterArmorThanEquipped(armor)
                            )
                            || (_lootingBrain.IsValuableEnough(rootItem) && availableGridSpaces > rootItem.GetItemSize())))
                    {
                        lootType = LootType.Item;
                    }
                }

                await UniTask.Yield(token);

                if (lootType is LootType.None || rootItem is null)
                {
                    await UniTask.Yield(token);

                    continue;
                }

                // If object has been ignored, skip to the next object detected
                if (_lootingBrain.IsLootIgnored(rootItem.Id))
                {
                    await UniTask.Yield(token);

                    continue;
                }

                var bounds = collider.bounds;
                var center = new Vector3(bounds.center.x, bounds.center.y - bounds.extents.y - 0.4f, bounds.center.z);
                var destination = GetDestination(center);

                await UniTask.Yield(token);

                // Check if loot is in range and sight
                if (!IsLootInRange(lootType, destination, out float dist) || !IsLootInSight(lootType, destination))
                {
                    if (dist != -1f && ++rangeCalculations >= maxRangeCalculations)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug("No loot in range, reached max calculations");
                        }

                        break;
                    }

                    if (dist == -1f && _botOwner.Mover == null)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug("Mover was null, stopping search");
                        }

                        break;
                    }

                    await UniTask.Yield(token);

                    continue;
                }

                // Cache the loot and set active target
                if (!ActiveLootCache.CacheActiveLootId(rootItem.Id, _botOwner))
                {
                    await UniTask.Yield(token);

                    continue;
                }

                _lootingBrain.DistanceToLoot = dist;
                _lootingBrain.Destination = destination;
                _lootingBrain.LootObjectPosition = interactableObject.transform.position;
                _lootingBrain.SetLoot(interactableObject, lootType);

                _emptyAttempts = 0;
                break;
            }
        }
        finally
        {
            if (!_lootingBrain.HasActiveLootable && ++_emptyAttempts > MaxEmptyAttempts)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Max empty attempts reached, preventing looting for {EmptyAttemptsCooldown}s");
                }
                OverrideNextScanTime(EmptyAttemptsCooldown);
                _emptyAttempts = 0;
            }

            _colliderPool.Return(colliders, true);
            ScanScheduler.Return(queue);
            _lootingBrain.ForceBrainEnabled = false;
            IsScanRunning = false;
        }
    }

    /**
    * Checks to see if any of the found lootable items are within their detection range specified in the mod settings.
    */
    private bool IsLootInRange(LootType lootType, Vector3 destination, out float dist)
    {
        if (destination == Vector3.zero || _botOwner.Mover == null)
        {
            if (_botOwner.Mover == null && _log.WarningEnabled)
            {
                _log.LogWarning("botOwner.BotMover is null! Cannot perform path distance calculations");
            }
            dist = -1f;
            return false;
        }

        dist = _botOwner.Mover.ComputePathLengthToPoint(destination);
        return lootType switch
        {
            LootType.Corpse => dist <= DetectCorpseDistance,
            LootType.Container => dist <= DetectContainerDistance,
            LootType.Item => dist <= DetectItemDistance,
            _ => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null)
        };
    }

    public bool IsLootInSight(LootType lootType, Vector3 destination)
    {
        var needsSight = lootType switch
        {
            LootType.Corpse => LootingBots.DetectCorpseNeedsSight.Value,
            LootType.Container => LootingBots.DetectContainerNeedsSight.Value,
            LootType.Item => LootingBots.DetectItemNeedsSight.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null)
        };
        if (!needsSight)
        {
            return true;
        }

        if (destination == Vector3.zero || _botOwner.LookSensor == null)
        {
            if (_botOwner.LookSensor == null && _log.WarningEnabled)
            {
                _log.LogWarning("botOwner.LookSensor is null! Cannot perform line of sight check");
            }
            return true;
        }

        Vector3 start = _botOwner.LookSensor.HeadPoint;
        Vector3 directionOfLoot = destination - start;

        bool sightBlocked = Physics.Raycast(start, directionOfLoot, directionOfLoot.magnitude, LayerMaskClass.HighPolyWithTerrainMask);

        return !sightBlocked;
    }

    private static Vector3 GetDestination(Vector3 center)
    {
        // Try to snap the desired destination point to the nearest NavMesh to ensure the bot can draw a navigable path to the point
        Vector3 pointNearbyContainer = NavMesh.SamplePosition(center, out NavMeshHit navMeshAlignedPoint, 1f, NavMesh.AllAreas)
            ? navMeshAlignedPoint.position
            : Vector3.zero;

        // Since SamplePosition always snaps to the closest point on the NavMesh, sometimes this point is a little too close to the loot and causes the bot to shake violently while looting.
        // Add a small amount of padding by pushing the point away from the nearbyPoint
        Vector3 padding = center - pointNearbyContainer;
        padding.y = 0;
        padding.Normalize();

        // Make sure the point is still snapped to the NavMesh after its been pushed
        Vector3 destination = NavMesh.SamplePosition(center - (padding * 1.5f), out navMeshAlignedPoint, 1f, navMeshAlignedPoint.mask)
            ? navMeshAlignedPoint.position
            : pointNearbyContainer;

        if (LootingBots.DebugLootNavigation.Value)
        {
            GameObjectHelper.DrawSphere(center, 0.5f, Color.red);
            GameObjectHelper.DrawSphere(pointNearbyContainer, 0.5f, Color.green);
            GameObjectHelper.DrawSphere(destination, 0.5f, Color.blue);
        }

        return destination;
    }

    private void ExceptionHandler(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug("Loot scan interrupted");
            }
            return;
        }
        if (_log.ErrorEnabled)
        {
            _log.LogError("Exception while trying to scan for loot:");
            _log.LogError(ex.ToString());
        }
    }
}
