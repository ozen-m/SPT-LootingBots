using EFT.InventoryLogic;
using LootingBots.Components;

namespace LootingBots.Actions;

/// <summary>
/// Base looting action
/// </summary>
public abstract class LootingAction
{
    /// <summary>
    /// Item operated upon
    /// </summary>
    public Item Item { get; set; }

    /// <summary>
    /// Value added to the bot's net worth
    /// </summary>
    public float NetWorthDelta { get; set; }

    public abstract Task<bool> ExecuteAsync(LootingTransactionController controller, CancellationToken token);

    public abstract void Return();

    protected virtual void Reset()
    {
        Item = null;
        NetWorthDelta = 0;
    }
}
