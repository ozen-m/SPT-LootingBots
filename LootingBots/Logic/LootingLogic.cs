using DrakiaXYZ.BigBrain.Brains;
using EFT;
using LootingBots.Components;
using LootingBots.Utilities;
using UnityEngine;
using UnityEngine.AI;

namespace LootingBots.Logic;

internal class LootingLogic(BotOwner botOwner) : CustomLogic(botOwner)
{
    private readonly LootingBrain _lootingBrain = botOwner.GetPlayer.gameObject.GetComponent<LootingBrain>();
    private readonly BotLog _log = new(LootingBots.LootLog, botOwner);
    private float _closeEnoughTimer;
    private float _moveTimer;
    private int _stuckCount;
    private int _navigationAttempts;
    private Vector3 _destination = Vector3.zero;

    // Run looting logic only when the bot is not looting and when the bot has an active item to loot
    private bool _shouldUpdate
    {
        get { return !_lootingBrain.LootTaskRunning && _lootingBrain.HasActiveLootable && BotOwner.BotState == EBotState.Active; }
    }

    public override void Update(CustomLayer.ActionData data)
    {
        // Kick off looting logic
        if (_shouldUpdate)
        {
            TryLoot();
        }
    }

    public override void Stop()
    {
        _destination = Vector3.zero;
        _lootingBrain.DistanceToLoot = float.MaxValue;
        _stuckCount = 0;
        _navigationAttempts = 0;
        _lootingBrain.StopLooting();
        base.Stop();
    }

    private void TryLoot()
    {
        try
        {
            // Check if the bot is close enough to the destination to commence looting
            if (_closeEnoughTimer < Time.time)
            {
                _closeEnoughTimer = Time.time + 2f;

                var isCloseEnough = IsCloseEnough();

                // If the bot is closer than 4m from the loot, they should slow down and not sprint to prevent powersliding
                var slowDown = _lootingBrain.DistanceToLoot < 6f;

                // If the bot has not just looted something, loot the current item since we are now close enough
                if (!_lootingBrain.LootTaskRunning && isCloseEnough && _lootingBrain.HasActiveLootable)
                {
                    // Crouch and look to item
                    BotOwner.SetPose(0f);
                    BotOwner.Steering.LookToPoint(_lootingBrain.LootObjectPosition);
                    _lootingBrain.StartLooting();
                    return;
                }

                if (!_lootingBrain.LootTaskRunning)
                {
                    // Stand and move to lootable
                    BotOwner.SetTargetMoveSpeed(1f);
                    BotOwner.SetPose(1f);
                    BotOwner.Steering.LookToMovingDirection();
                }

                // Stop the bot from sprinting when approaching lootable
                if (slowDown)
                {
                    BotOwner.Mover.Sprint(false);
                }
            }

            // Try to move the bot to the destination
            if (_moveTimer < Time.time && !_lootingBrain.LootTaskRunning)
            {
                _moveTimer = Time.time + 4f;

                // Initiate move to loot. Will return false if the bot is not able to navigate using a NavMesh
                var canMove = TryMoveToLoot();

                // If there is not a valid path to the loot, ignore the loot forever
                if (!canMove)
                {
                    _lootingBrain.HandleNonNavigableLoot();
                    _stuckCount = 0;
                }
            }
        }
        catch (Exception e)
        {
            _log.LogError(e);
        }
    }

    /// <summary>
    /// Check to see if the destination point and the loot object do not have a wall between them by casting a Ray between the two points.
    /// Walls should be on the LowPolyCollider LayerMask, so we can assume if we see one of these then we cannot properly loot.
    /// </summary>
    /// <returns></returns>
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
    /// Navigation will be canceled if:
    /// - The bot has not moved in more than 2 navigation calls
    /// - If the destination cannot be snapped to a mesh,
    /// - If the NavPathStatus is Invalid
    /// </summary>
    public bool TryMoveToLoot()
    {
        var canMove = true;
        try
        {
            // Increment navigation attempt counter
            _navigationAttempts++;

            var lootableName = _lootingBrain.ActiveLoot.GetLootName();

            // If the bot has not been stuck for more than 2 navigation checks, attempt to navigate to the lootable otherwise ignore the container forever
            var isBotStuck = _stuckCount > 1;
            var isNavigationLimit = _navigationAttempts > 30;

            // Log every 5 movement attempts to reduce noise
            if (_navigationAttempts % 5 == 1 && _log.DebugEnabled)
            {
                _log.LogDebug($"[Attempt: {_navigationAttempts}] Navigating to {lootableName}");
            }

            if (!isBotStuck && !isNavigationLimit && _lootingBrain.Destination != Vector3.zero)
            {
                _destination = _lootingBrain.Destination;

                if (_navigationAttempts == 1)
                {
                    var pathStatus = BotOwner.GoToPoint(_destination, true, -1f, false, false);

                    if (pathStatus == NavMeshPathStatus.PathInvalid)
                    {
                        if (_log.WarningEnabled)
                        {
                            _log.LogWarning($"No valid path to: {lootableName}. Ignoring");
                        }

                        canMove = false;
                    }

                    if (pathStatus == NavMeshPathStatus.PathPartial)
                    {
                        if (_log.WarningEnabled)
                        {
                            _log.LogWarning($"Partial path to: {lootableName}.");
                        }
                    }
                }
            }
            else
            {
                if (isBotStuck)
                {
                    if (_log.WarningEnabled)
                    {
                        _log.LogWarning($"Has been stuck trying to reach: {lootableName}. Ignoring");
                    }
                }
                else if (_log.WarningEnabled)
                {
                    _log.LogWarning($"Has exceeded the navigation limit (30) trying to reach: {lootableName}. Ignoring");
                }
                canMove = false;
            }
        }
        catch (Exception e)
        {
            _log.LogError(e);
        }

        return canMove;
    }

    /// <summary>
    /// Check to see if the bot is close enough to the destination so that they can stop moving and start looting.
    /// </summary>
    private bool IsCloseEnough()
    {
        if (_destination == Vector3.zero)
        {
            return false;
        }

        // Calculate distance from bot to destination
        var vector = BotOwner.Position - _destination;
        var y = vector.y;
        vector.y = 0f;
        var distance = vector.sqrMagnitude;

        // Within a radius of 0.92 (sqr 0.85), and ±0.5 vertically
        var isCloseEnough = distance < 0.85f && Math.Abs(y) < 0.5f;

        // Check to see if the bot is stuck
        if (!IsBotStuck(distance))
        {
            // Bot has moved, reset stuckCount and update cached distance to container
            _stuckCount = 0;
            _lootingBrain.DistanceToLoot = distance;
        }

        if (isCloseEnough && _log.DebugEnabled)
        {
            _log.LogDebug($"Bot is close enough to loot. {distance}. height diff: {y}");
        }

        return isCloseEnough;
    }

    /// <summary>
    /// Checks if the bot is stuck moving and increments the stuck counter.
    /// </summary>
    /// <param name="dist">Current distance</param>
    private bool IsBotStuck(float dist)
    {
        // Calculate change in distance and assume any change less than .25f means the bot hasn't moved.
        var changeInDist = Math.Abs(_lootingBrain.DistanceToLoot - dist);
        var isStuck = changeInDist < 0.3f;

        if (isStuck)
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug($"[Stuck: {_stuckCount}] Distance moved since check: {changeInDist}. Dist from loot: {dist}");
            }

            // Bot is stuck, update stuck count
            _stuckCount++;
        }

        return isStuck;
    }
}
