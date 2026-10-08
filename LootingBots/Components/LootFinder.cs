using System.Buffers;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.InventoryLogic;
using LootingBots.Patches;
using LootingBots.Utilities;
using LootingBots.Utilities.Comparers;
using LootingBots.Utilities.Extensions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

namespace LootingBots.Components;

public class LootFinder : MonoBehaviour
{
    private static readonly ArrayPool<Collider> _colliderPool = ArrayPool<Collider>.Shared;

    private LootingBrain _lootingBrain;
    private BotOwner _botOwner;
    private BotLog _log;

    private float _scanTimer;
    private bool _lockUntilNextScan;

    // Bot specific config
    private bool _containerLootingEnabled;
    private bool _needsContainerSight;
    private bool _itemLootingEnabled;
    private bool _needsItemSight;
    private bool _corpseLootingEnabled;
    private bool _needsCorpseSight;

    public bool IsScheduledScan
    {
        get { return _scanTimer < Time.time; }
    }

    public bool IsScanRunning
    {
        get { return _lootTask is not null && !_lootTask.IsCompleted; }
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

    private readonly Queue<LootableContainer> _priorityLootableContainers = [];
    private readonly Queue<Player> _priorityCorpses = [];

    private Task _lootTask;
    private CancellationTokenSource _lootFinderCts;
    private GameObject[] _debugSpheres;

    public void Init(BotOwner botOwner)
    {
        _scanTimer = Time.time + LootingBots.InitialStartTimer.Value;
        _botOwner = botOwner;
        _lootingBrain = _botOwner.GetPlayer.gameObject.GetComponent<LootingBrain>();
        _log = new BotLog(LootingBots.LootLog, _botOwner);
        _lootFinderCts = new CancellationTokenSource();

        UpdateFinderSettings();

        OnAirdropLandedPatch.OnAirdropLanded += OnAirdropLanded;
        _botOwner.BotPersonalStats.OnKillTarget += OnKilledEnemyPlayer;
    }

    public void UpdateFinderSettings()
    {
        _corpseLootingEnabled = LootingBots.CorpseLootingEnabled.Value.IsBotEnabled(_lootingBrain);
        _needsCorpseSight = LootingBots.DetectCorpseNeedsSight.Value.IsBotEnabled(_lootingBrain);
        _containerLootingEnabled = LootingBots.ContainerLootingEnabled.Value.IsBotEnabled(_lootingBrain);
        _needsContainerSight = LootingBots.DetectContainerNeedsSight.Value.IsBotEnabled(_lootingBrain);
        _itemLootingEnabled = LootingBots.LooseItemLootingEnabled.Value.IsBotEnabled(_lootingBrain);
        _needsItemSight = LootingBots.DetectItemNeedsSight.Value.IsBotEnabled(_lootingBrain);
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
        StopFindingLoot();
        if (_lootFinderCts.IsCancellationRequested)
        {
            _lootFinderCts.Dispose();
            _lootFinderCts = new CancellationTokenSource();
        }

        if (!FindPrioritizedLoot(ticket))
        {
            _lootTask = FindLootAsync(ticket, _lootFinderCts.Token);
        }

        SetLockUntilNextScan(false);
    }

    public void ForceScan()
    {
        _scanTimer = -1f;
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

    public void StopFindingLoot()
    {
        if (!IsScanRunning)
        {
            return;
        }

        _lootFinderCts.Cancel();
    }

    public void EnqueuePriorityCorpse(string corpseProfileId)
    {
        var playerOwner = Singleton<GameWorld>.Instance.GetEverExistedBridgeByProfileID(corpseProfileId);
        if (playerOwner?.iPlayer is Player deadPlayer)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Adding [{deadPlayer.name}] to priority queue");
            }

            _priorityCorpses.Enqueue(deadPlayer);
        }
        else
        {
            if (_log.ErrorEnabled)
            {
                _log.LogError($"Cannot prioritize corpse, player not found! ProfileId: {corpseProfileId}");
            }
        }
    }

    public void OnDestroy()
    {
        StopFindingLoot();
        _lootFinderCts.Dispose();

        OnAirdropLandedPatch.OnAirdropLanded -= OnAirdropLanded;
        _botOwner.BotPersonalStats.OnKillTarget -= OnKilledEnemyPlayer;

        if (_debugSpheres is not null)
        {
            foreach (var sphere in _debugSpheres)
            {
                Destroy(sphere);
            }
        }
    }

    private async Task FindLootAsync(int queue, CancellationToken token)
    {
        var colliders = _colliderPool.Rent(3072);
        var seen = HashSetPool<InteractableObject>.Get();
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
            var detectionRadius = Mathf.Max(DetectItemDistance, DetectContainerDistance);
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

            await Task.Yield();

            if (hits == 0)
            {
                if (_log.DebugEnabled)
                {
                    _log.LogDebug("No loot in range");
                }
                return;
            }

            // Sort colliders by distance
            ColliderDistanceComparer.Instance.SetReferencePosition(botPosition);
            Array.Sort(colliders, 0, hits, ColliderDistanceComparer.Instance);

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"Scan results: {hits}");
            }

            await Task.Yield();

            const int maxRangeCalculations = 3;
            var rangeCalculations = 0;

            // Process sorted colliders
            for (var i = 0; i < hits; i++)
            {
                token.ThrowIfCancellationRequested();

                var collider = colliders[i];

                Item rootItem = null;
                var lootType = LootType.None;

                // Get InteractableObject once and check derived type
                var interactableObject = collider.gameObject.GetComponentInParent<InteractableObject>();
                if (interactableObject == null || !seen.Add(interactableObject))
                {
                    await Task.Yield();

                    continue;
                }

                if (_corpseLootingEnabled && interactableObject is Corpse corpse)
                {
                    var player = collider.gameObject.GetComponentInParent<Player>(); // Corpse is a bot corpse and not a static "Dead scav"
                    if (player != null && corpse.Item is InventoryEquipment equipment)
                    {
                        rootItem = equipment;
                        lootType = LootType.Corpse;
                    }
                }
                else if (_containerLootingEnabled && interactableObject is LootableContainer container)
                {
                    rootItem = container.ItemOwner.RootItem;
                    if (container.isActiveAndEnabled && container.DoorState is not EDoorState.Locked)
                    {
                        lootType = LootType.Container;
                    }
                }
                else if (_itemLootingEnabled && interactableObject is LootItem lootItem && lootItem is not Corpse)
                {
                    rootItem = lootItem.Item;
                    if (rootItem is not null && !rootItem.QuestItem)
                    {
                        lootType = LootType.Item;
                    }
                }

                if (lootType is LootType.None || rootItem is null)
                {
                    await Task.Yield();

                    continue;
                }

                // If object has been ignored, skip to the next object detected
                var rootItemId = rootItem.Id;
                if (_lootingBrain.IsLootIgnored(rootItemId) || ActiveLootCache.IsLootInUse(rootItemId, _botOwner))
                {
                    await Task.Yield();

                    continue;
                }

                // Push the center point down by .4f to help container positions of jackets snap to a valid NavMesh.
                var center = interactableObject.TrackableTransform.position;
                center.y -= 0.4f;

                // Check if we can navigate to the interactable object
                if (!GetDestination(center, out var destination))
                {
                    // Ignore this loot since it's non-navigable
                    _lootingBrain.IgnoreLoot(rootItemId);

                    await Task.Yield();

                    continue;
                }

                // Check if we can perform distance and LOS checks
                if (_botOwner.Mover is null)
                {
                    if (_log.WarningEnabled)
                    {
                        _log.LogWarning("botOwner.BotMover is null! Cannot perform path distance calculations");
                    }

                    return;
                }
                if (_botOwner.LookSensor is null)
                {
                    if (_log.WarningEnabled)
                    {
                        _log.LogWarning("botOwner.LookSensor is null! Cannot perform line of sight check");
                    }

                    return;
                }

                // Check if loot is in sight
                if (!IsLootInSight(lootType, destination))
                {
                    await Task.Yield();

                    continue;
                }

                // Check if loot is in range
                if (!IsLootInRange(lootType, destination, out var dist))
                {
                    // Found path to loot but not within range
                    if (dist != -1f && ++rangeCalculations >= maxRangeCalculations)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug("No loot in range, reached max calculations");
                        }

                        break;
                    }
                    await Task.Yield();

                    continue;
                }

                // Cache the loot and set active target
                if (!ActiveLootCache.CacheActiveLootId(rootItemId, _botOwner))
                {
                    if (_log.ErrorEnabled)
                    {
                        _log.LogError("Failed to cache and set active loot, bot owner is null or id already in the cache?");
                    }
                    await Task.Yield();

                    continue;
                }

                _lootingBrain.SetLoot(interactableObject, lootType, center, destination, rootItemId, dist);
                return;
            }

            if (_log.DebugEnabled)
            {
                _log.LogDebug($"No loot in range, preventing looting for {LootingBots.NoLootCooldown.Value}s");
            }
            OverrideNextScanTime(Mathf.Max(LootingBots.NoLootCooldown.Value, LootingBots.LootScanInterval.Value));
        }
        catch (OperationCanceledException)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug("Loot scan interrupted");
            }
        }
        catch (Exception e)
        {
            if (_log.ErrorEnabled)
            {
                _log.LogError("Exception while trying to scan for loot:");
                _log.LogError(e.ToString());
            }
        }
        finally
        {
            _colliderPool.Return(colliders, true);
            HashSetPool<InteractableObject>.Release(seen);
            ScanScheduler.Return(queue);
            _lootingBrain.ForceBrainEnabled = false;
        }
    }

    private bool FindPrioritizedLoot(int ticket)
    {
        if (_containerLootingEnabled)
        {
            for (var i = 0; i < _priorityLootableContainers.Count; i++)
            {
                var lootableContainer = _priorityLootableContainers.Dequeue();

                var position = lootableContainer.TrackableTransform.position;
                if (!GetDestination(position, out var destination))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Could not get destination for container [{lootableContainer.GetLootName()}]");
                    }
                    continue;
                }

                if (!IsLootInRange(LootType.Container, destination, out var dist))
                {
                    if (dist != -1f)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug($"Re-queuing container [{lootableContainer.GetLootName()}], not in range. Dist: {dist}");
                        }
                        _priorityLootableContainers.Enqueue(lootableContainer);
                    }
                    continue;
                }

                // Cache the loot and set active target
                var rootItemId = lootableContainer.GetRootItemId();
                if (!ActiveLootCache.CacheActiveLootId(rootItemId, _botOwner))
                {
                    if (_log.ErrorEnabled)
                    {
                        _log.LogError("Failed to cache and set active loot, bot owner is null or id already in the cache?");
                    }
                    continue;
                }

                _lootingBrain.SetLoot(lootableContainer, LootType.Container, position, destination, rootItemId, dist);

                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Setting container [{lootableContainer.GetLootName()}] as active loot. Dist: {dist}");
                }

                ScanScheduler.Return(ticket);
                _lootingBrain.ForceBrainEnabled = false;
                return true;
            }
        }

        if (_corpseLootingEnabled)
        {
            for (var i = 0; i < _priorityCorpses.Count; i++)
            {
                var player = _priorityCorpses.Dequeue();
                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Trying to find prioritized corpse: {player.AIData?.BotOwner.Name()}");
                }

                var corpse = LootUtils.PlayerCorpseField(player);
                if (corpse == null)
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug(
                            $"Removing prioritized player, corpse not found for killed player [{player.AIData?.BotOwner.Name()}]"
                        );
                    }

                    continue;
                }

                // If corpse has been ignored, continue to the next prioritized corpse
                var rootItemId = corpse.GetRootItemId();
                if (_lootingBrain.IsLootIgnored(rootItemId))
                {
                    continue;
                }
                if (ActiveLootCache.IsLootInUse(rootItemId, _botOwner))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Re-queuing corpse [{corpse.GetLootName()}], is currently being looted by someone else");
                    }
                    _priorityCorpses.Enqueue(player);
                    continue;
                }

                var position = corpse.TrackableTransform.position;
                if (!GetDestination(position, out var destination))
                {
                    if (_log.DebugEnabled)
                    {
                        _log.LogDebug($"Could not get destination for corpse [{corpse.GetLootName()}]");
                    }
                    continue;
                }

                // Check if loot is in range
                // No need to check LOS since technically it's their kill
                if (!IsLootInRange(LootType.Corpse, destination, out var dist))
                {
                    if (dist != -1f)
                    {
                        if (_log.DebugEnabled)
                        {
                            _log.LogDebug($"Re-queuing corpse [{corpse.GetLootName()}], not in range. Dist: {dist}");
                        }
                        _priorityCorpses.Enqueue(player);
                    }
                    continue;
                }

                // Cache the loot and set active target
                if (!ActiveLootCache.CacheActiveLootId(rootItemId, _botOwner))
                {
                    if (_log.ErrorEnabled)
                    {
                        _log.LogError("Failed to cache and set active loot, bot owner is null or id already in the cache?");
                    }
                    continue;
                }

                _lootingBrain.SetLoot(corpse, LootType.Corpse, position, destination, rootItemId, dist);

                if (_log.DebugEnabled)
                {
                    _log.LogDebug($"Setting Corpse [{corpse.GetLootName()}] as active loot. Dist: {dist}");
                }

                ScanScheduler.Return(ticket);
                _lootingBrain.ForceBrainEnabled = false;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks to see if any of the found lootable items are within their detection range specified in the mod settings.
    /// </summary>
    private bool IsLootInRange(LootType lootType, Vector3 destination, out float dist)
    {
        if (destination == Vector3.zero)
        {
            dist = -1f;
            return false;
        }

        var maxRange = lootType switch
        {
            LootType.Corpse => DetectCorpseDistance,
            LootType.Container => DetectContainerDistance,
            LootType.Item => DetectItemDistance,
            LootType.None => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null),
            _ => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null),
        };

        var path = _botOwner.Mover.CalcPath(destination);
        return path.CalculatePathLengthWithMaxRange(maxRange, out dist);
    }

    private bool IsLootInSight(LootType lootType, Vector3 destination)
    {
        var needsSight = lootType switch
        {
            LootType.Corpse => _needsCorpseSight,
            LootType.Container => _needsContainerSight,
            LootType.Item => _needsItemSight,
            LootType.None => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null),
            _ => throw new ArgumentOutOfRangeException(nameof(lootType), lootType, null),
        };
        if (!needsSight)
        {
            return true;
        }

        if (destination == Vector3.zero)
        {
            return false;
        }

        var start = _botOwner.LookSensor.HeadPoint;
        var directionOfLoot = destination - start;

        var sightBlocked = Physics.Raycast(start, directionOfLoot, directionOfLoot.magnitude, LayersMaskController.HighPolyWithTerrainMask);

        return !sightBlocked;
    }

    private bool GetDestination(Vector3 center, out Vector3 destination)
    {
        // Try to snap the desired destination point to the nearest NavMesh to ensure the bot can draw a navigable path to the point
        if (!NavMesh.SamplePosition(center, out var navMeshAlignedPoint, 1.5f, NavMesh.AllAreas))
        {
            destination = Vector3.zero;
            return false;
        }

        // Since SamplePosition always snaps to the closest point on the NavMesh,
        // sometimes this point is a little too close to the loot and causes the bot to shake violently while looting.
        // So add a small amount of padding by pushing the point away from the nearbyPoint.
        var pointNearbyContainer = navMeshAlignedPoint.position;
        var direction = center - pointNearbyContainer;
        direction.y = 0;
        if (direction.sqrMagnitude < 0.001f)
        {
            // pointNearbyContainer didn't move horizontally so we can't add padding, fallback to the bot's position
            direction = center - _botOwner.Position;
            direction.y = 0;
        }

        // Make sure the point is still snapped to the NavMesh after it's been pushed
        destination = NavMesh.SamplePosition(center - (direction.normalized * 1f), out navMeshAlignedPoint, 1.5f, navMeshAlignedPoint.mask)
            ? navMeshAlignedPoint.position
            : pointNearbyContainer;

        if (LootingBots.DebugLootNavigation.Value)
        {
            _debugSpheres ??= CreateDebugSpheres();
            _debugSpheres[0].transform.position = center; // red
            _debugSpheres[1].transform.position = pointNearbyContainer; // green
            _debugSpheres[2].transform.position = destination; // blue
        }

        return true;
    }

    private void OnAirdropLanded(LootableContainer airdrop)
    {
        if (_log.DebugEnabled)
        {
            _log.LogDebug($"Adding [{airdrop.GetLootName()}] to priority queue");
        }

        _priorityLootableContainers.Enqueue(airdrop);
    }

    private void OnKilledEnemyPlayer(string victimProfileId, DamageInfo damageInfo)
    {
        EnqueuePriorityCorpse(victimProfileId);
    }

    private static GameObject[] CreateDebugSpheres()
    {
        var debugSpheres = new GameObject[3];
        debugSpheres[0] = GameObjectHelper.DrawSphere(Vector3.zero, 0.35f, Color.red); // center
        debugSpheres[1] = GameObjectHelper.DrawSphere(Vector3.zero, 0.35f, Color.green); // pointNearbyContainer
        debugSpheres[2] = GameObjectHelper.DrawSphere(Vector3.zero, 0.35f, Color.blue); // destination
        return debugSpheres;
    }

    public enum LootType : byte
    {
        None = 0,
        Corpse = 1,
        Container = 2,
        Item = 3,
    }
}

public static class PathExtensions
{
    /// <summary>
    /// Based on <see cref="NavMeshPathExtension.CalculatePathLength(Vector3[] corners)"/>
    /// </summary>
    public static bool CalculatePathLengthWithMaxRange(this Vector3[] corners, float range, out float length)
    {
        if (corners is null || corners.Length < 2)
        {
            length = -1f;
            return false;
        }

        length = 0f;
        var prevCorner = corners[0];
        for (var i = 1; i < corners.Length; i++)
        {
            var currentCorner = corners[i];
            length += Vector3.Distance(prevCorner, currentCorner);

            // Reached max range
            if (length > range)
            {
                return false;
            }

            prevCorner = currentCorner;
        }

        return true;
    }
}
