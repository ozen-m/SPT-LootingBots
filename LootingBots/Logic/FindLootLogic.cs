using DrakiaXYZ.BigBrain.Brains;
using EFT;
using LootingBots.Components;
using LootingBots.Utilities;

namespace LootingBots.Logic;

internal class FindLootLogic : CustomLogic
{
    private readonly LootingBrain _lootingBrain;
    private readonly LootFinder _lootFinder;
    private readonly BotLog _log;

    public FindLootLogic(BotOwner botOwner)
        : base(botOwner)
    {
        _lootingBrain = botOwner.GetPlayer.gameObject.GetComponent<LootingBrain>();
        _lootFinder = botOwner.GetPlayer.gameObject.GetComponent<LootFinder>();
        _log = new BotLog(LootingBots.LootLog, botOwner);

        if (botOwner.Profile.Nickname != _lootingBrain.BotOwner.Profile.Nickname)
        {
            _log.LogError(botOwner.Profile.Nickname + " is using the LootingBrain for " + _lootingBrain.BotOwner.Profile.Nickname);
        }
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (!_lootingBrain.HasFreeSpace)
        {
            // Need to disable LockUntilNextScan if the bot has no free space to prevent an infinite looting loop
            _lootFinder.SetLockUntilNextScan(false);

            return;
        }

        // Trigger a scan if one is not running already
        if (!_lootFinder.IsScanRunning && ScanScheduler.CanStartScan(out var ticket))
        {
            if (_log.DebugEnabled)
            {
                _log.LogDebug(
                    $"Starting scan ({ticket}) - free space: {_lootingBrain.HasFreeSpace}. isScanRunning: {_lootFinder.IsScanRunning}"
                );
            }
            _lootFinder.BeginSearch(ticket);
        }
    }

    public override void Stop()
    {
        _lootFinder.ResetScanTimer();
        _lootFinder.StopFindingLoot();
        base.Stop();
    }
}
