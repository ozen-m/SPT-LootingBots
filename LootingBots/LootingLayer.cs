using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using LootingBots.Components;
using LootingBots.Logic;
using LootingBots.Utilities.Extensions;
using UnityEngine;

namespace LootingBots;

internal class LootingLayer : CustomLayer
{
    private readonly LootingBrain _lootingBrain;
    private readonly LootFinder _lootFinder;

    private readonly Action _lootingLogic;
    private readonly Action _findLootLogic;
    private readonly Action _peacefulLogic;

    public LootingLayer(BotOwner botOwner, int priority)
        : base(botOwner, priority)
    {
        var lootingBrain = botOwner.GetPlayer.gameObject.AddComponent<LootingBrain>();
        var lootFinder = botOwner.GetPlayer.gameObject.AddComponent<LootFinder>();
        lootingBrain.Init(botOwner);
        lootFinder.Init(botOwner);

        _lootingBrain = lootingBrain;
        _lootFinder = lootFinder;

        _lootingLogic = new Action(typeof(LootingLogic), "Looting");
        _findLootLogic = new Action(typeof(FindLootLogic), "Loot Scan");
        _peacefulLogic = new Action(typeof(PeacefulLogic), "Peaceful");
    }

    public override string GetName()
    {
        return "Looting";
    }

    public override bool IsActive()
    {
        return BotOwner.BotState == EBotState.Active // Bot is active
            && BotOwner.Memory.IsPeace // Bot does not have an enemy
            && (!BotOwner.Medecine.FirstAid.Have2Do && !BotOwner.Medecine.SurgicalKit.HaveWork) // Bot is not healing
            && _lootingBrain.IsBrainEnabled
            && (_lootFinder.IsScheduledScan || _lootingBrain.IsBotLooting);
    }

    public override void Start()
    {
        BotOwner.PatrollingData.Pause();
        base.Start();
    }

    public override void Stop()
    {
        BotOwner.PatrollingData.Unpause();
        base.Stop();
    }

    public override Action GetNextAction()
    {
        if (_lootingBrain.IsBotLooting)
        {
            return _lootingLogic;
        }

        if (_lootFinder.IsScheduledScan)
        {
            return _findLootLogic;
        }

        return _peacefulLogic;
    }

    public override bool IsCurrentActionEnding()
    {
        if (CurrentAction == _findLootLogic)
        {
            return !_lootFinder.IsScanRunning;
        }

        var notLooting = !_lootingBrain.IsBotLooting;

        if (CurrentAction == _lootingLogic && notLooting)
        {
            // Reset scan timer once looting has completed
            _lootFinder.ResetScanTimer();
        }

        return notLooting;
    }

    public override void BuildDebugText(StringBuilder debugPanel)
    {
        var lootName = _lootingBrain.ActiveLoot != null ? _lootingBrain.ActiveLoot.GetLootName() : "-";

        debugPanel.AppendLine(
            _lootingBrain.LootTaskRunning ? "Looting in progress..."
                : _lootFinder.IsScanRunning ? "Scan in progress..."
                : string.Empty,
            Color.green
        );
        debugPanel.AppendLabeledValue("Target Loot", $" {lootName} ({_lootingBrain.ActiveLootType})", Color.yellow, Color.yellow);

        debugPanel.AppendLabeledValue(
            "Distance to Loot",
            $" {(_lootingBrain.ActiveLootType is LootFinder.LootType.None || _lootingBrain.DistanceToLoot == float.MaxValue ? "Calculating path..." : $"{Mathf.Sqrt(_lootingBrain.DistanceToLoot):0.##}m")}",
            Color.grey,
            Color.grey
        );

        _lootingBrain.Stats.StatsDebugPanel(debugPanel);
    }
}
