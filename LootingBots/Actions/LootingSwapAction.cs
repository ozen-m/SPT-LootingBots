using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;

namespace LootingBots.Actions;

public class LootingSwapAction(
    Item toThrow,
    Item toEquip = null,
    Func<CancellationToken, UniTask> callback = null,
    Func<CancellationToken, UniTask> onComplete = null
)
{
    public Item ToThrow { get; private set; } = toThrow;
    public Item ToEquip { get; private set; } = toEquip;
    public Func<CancellationToken, UniTask> Callback { get; private set; } = callback;
    public Func<CancellationToken, UniTask> OnComplete { get; private set; } = onComplete;
}
