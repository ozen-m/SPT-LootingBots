using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;

namespace LootingBots.Actions;

public class LootingMoveAction(
    Item toMove,
    ItemAddress place = null,
    Item toItem = null,
    Func<CancellationToken, UniTask> callback = null,
    Func<CancellationToken, UniTask> onComplete = null
)
{
    public Item ToMove { get; private set; } = toMove;
    public ItemAddress Place { get; private set; } = place;
    public Item ToItem { get; private set; } = toItem;
    public Func<CancellationToken, UniTask> Callback { get; private set; } = callback;
    public Func<CancellationToken, UniTask> OnComplete { get; private set; } = onComplete;
}
