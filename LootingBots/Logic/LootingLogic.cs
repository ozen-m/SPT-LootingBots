using DrakiaXYZ.BigBrain.Brains;
using EFT;
using LootingBots.Components;
using LootingBots.Utilities;
using UnityEngine;
using UnityEngine.AI;

namespace LootingBots.Logic;

internal class LootingLogic : CustomLogic
{
    private readonly LootingBrain _lootingBrain;
    private readonly BotLog _log;
    private Vector3 _destination = Vector3.zero;
    private float _updateTimer;
    private float _stuckTimer;
    private int _stuckCount;
    private int _navigationAttempts;

    // Run looting logic only when the bot is not looting
    private bool ShouldUpdate
    {
        get { return !_lootingBrain.LootTaskRunning && BotOwner.BotState == EBotState.Active; }
    }

    public LootingLogic(BotOwner botOwner)
        : base(botOwner)
    {
        _lootingBrain = botOwner.GetPlayer.gameObject.GetComponent<LootingBrain>();
        _log = new BotLog(LootingBots.LootLog, botOwner);

        if (botOwner.Profile.Nickname != _lootingBrain.BotOwner.Profile.Nickname)
        {
            _log.LogError(botOwner.Profile.Nickname + " is using the LootingBrain for " + _lootingBrain.BotOwner.Profile.Nickname);
        }
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (!ShouldUpdate)
        {
            return;
        }

        // Open any nearby door
        BotOwner.DoorOpener.UpdateDoorInteractionStatus();

        if (_updateTimer > Time.time)
        {
            return;
        }
        _updateTimer = Time.time + 0.2f;

        // If a player picks up an item that was marked as active by a bot, its ItemOwner?.RootItem will be null.
        // In this case cleanup the active item
        if (_lootingBrain.ActiveLootType == LootFinder.LootType.Item && _lootingBrain.ActiveLoot.GetRootItem() == null)
        {
            _lootingBrain.CleanupLoot(false);
            return;
        }

        // Kick off looting logic
        TryLoot();
    }

    public override void Start()
    {
        _destination = _lootingBrain.Destination;
    }

    public override void Stop()
    {
        _destination = Vector3.zero;
        _stuckCount = 0;
        _navigationAttempts = 0;
        _lootingBrain.DistanceToLoot = float.MaxValue;
        _lootingBrain.StopLooting();
        base.Stop();
    }

    private void TryLoot()
    {
        // Check if the bot is close enough to the destination to commence looting
        var isCloseEnough = IsCloseEnough();
        if (isCloseEnough)
        {
            // Crouch and look to item
            BotOwner.SetPose(0f);
            BotOwner.Steering.LookToPoint(_lootingBrain.LootObjectPosition, 180f);
            _lootingBrain.StartLooting();
            return;
        }

        // Try moving to loot. Will return false if the bot is not able to navigate
        var canMove = TryMoveToLoot();
        if (!canMove)
        {
            // There is no valid path to the loot, ignore the loot forever
            _lootingBrain.CleanupLoot();
            _stuckCount = 0;
            return;
        }

        // Stand and move to lootable
        BotOwner.SetTargetMoveSpeed(1f);
        BotOwner.SetPose(1f);
        BotOwner.Steering.LookToMovingDirection();

        // If the bot is closer than 5m (sqr 25f) from the loot, they should slow down to prevent power-sliding, otherwise sprint
        var canSprint = _lootingBrain.DistanceToLoot > 25f && BotOwner.Mover.CurrentState != EBotMoverState.NearDoor;
        BotOwner.Mover.Sprint(canSprint);
    }

    /// <summary>
    /// Check to see if the destination point and the loot object do not have a wall between them by casting a Ray between the two points.
    /// Walls should be on the LowPolyCollider LayerMask, so we can assume if we see one of these then we cannot properly loot.
    /// </summary>
    public bool HasLOS()
    {
        var rayDirection = _lootingBrain.LootObjectPosition - _destination;

        if (Physics.Raycast(_destination, rayDirection, out var hit) && hit.collider.gameObject.layer == LootUtils.LowPolyMask)
        {
            if (_log.ErrorEnabled)
            {
                _log.LogError($"NO LOS: LowPolyCollider hit {hit.collider.gameObject.layer} {hit.collider.gameObject.name}");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Makes the bot look towards the target destination and begin moving towards it.
    /// Navigation will be canceled and loot is ignored if:
    /// - The bot has not moved in more than 2 stuck checks (first stuck is almost always stopping at a door)
    /// - If the destination cannot be snapped to a mesh
    /// - If the NavPathStatus is Invalid
    /// </summary>
    public bool TryMoveToLoot()
    {
        var isBotStuck = _stuckCount > 1;
        if (isBotStuck)
        {
            if (_log.WarningEnabled)
            {
                _log.LogWarning(
                    $"Has been stuck trying to reach: {_lootingBrain.ActiveLoot.GetLootName()}. Remaining distance: {Mathf.Sqrt(_lootingBrain.DistanceToLoot)}. Ignoring"
                );
            }

            return false;
        }

        // If the bot is interacting with a door, let it complete first
        if (BotOwner.DoorOpener.Interacting)
        {
            return true;
        }

        // Instruct the bot to move to the destination: on first attempt, or if the bot has no path
        if (_navigationAttempts == 0 || !BotOwner.Mover.HasPathAndNoComplete)
        {
            // Increment navigation attempt counter
            _navigationAttempts++;

            // Log every 5 movement attempts to reduce noise
            if (_log.DebugEnabled && _navigationAttempts % 5 == 1)
            {
                _log.LogDebug($"[Attempt: {_navigationAttempts}] Navigating to {_lootingBrain.ActiveLoot.GetLootName()}");
            }

            var pathStatus = BotOwner.GoToPoint(_destination, true, -1f, false, false);
            if (pathStatus == NavMeshPathStatus.PathInvalid)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"No valid path to: {_lootingBrain.ActiveLoot.GetLootName()}. Ignoring");
                }

                return false;
            }
            if (pathStatus == NavMeshPathStatus.PathPartial)
            {
                if (_log.WarningEnabled)
                {
                    _log.LogWarning($"Partial path to: {_lootingBrain.ActiveLoot.GetLootName()}.");
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Check to see if the bot is close enough to the destination so that they can stop moving and start looting.
    /// </summary>
    private bool IsCloseEnough()
    {
        // Calculate distance from bot to destination
        var vector = BotOwner.Position - _destination;
        var y = vector.y;
        vector.y = 0f;
        var sqrDistance = vector.sqrMagnitude;

        // Within a radius of 0.92 (sqr 0.85), and ±0.5 vertically
        var isCloseEnough = sqrDistance < 0.85f && Math.Abs(y) < 0.5f;

        // Check to see if the bot is stuck
        if (!IsBotStuck(sqrDistance))
        {
            // Bot has moved, reset stuckCount and update cached distance to container
            _stuckCount = 0;
            _lootingBrain.DistanceToLoot = sqrDistance;
        }

        if (isCloseEnough && _log.DebugEnabled)
        {
            _log.LogDebug($"Bot is close enough to loot. {Mathf.Sqrt(sqrDistance)}. height diff: {y}");
        }

        return isCloseEnough;
    }

    /// <summary>
    /// Checks if the bot is stuck moving and increments the stuck counter.
    /// </summary>
    /// <param name="sqrDist">Current squared distance</param>
    private bool IsBotStuck(float sqrDist)
    {
        // If the bot is interacting with a door, do not consider as stuck
        if (BotOwner.DoorOpener.Interacting)
        {
            return false;
        }

        // Calculate change in distance and assume any change more than 0f means the bot has moved.
        var changeInDistSqr = Mathf.Abs(_lootingBrain.DistanceToLoot - sqrDist);
        var isStuck = changeInDistSqr < float.Epsilon;

        // Only increment stuck count every 2 seconds
        if (_stuckTimer < Time.time && isStuck)
        {
            // Bot is stuck, update stuck count
            _stuckTimer = Time.time + 2f;
            _stuckCount++;

            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"[Stuck: {_stuckCount}] Distance moved since check: {Mathf.Sqrt(changeInDistSqr)}. Dist from loot: {Mathf.Sqrt(sqrDist)}"
                );
            }
        }

        return isStuck;
    }
}
