using Comfort.Common;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using LootingBots.Components;
using LootingBots.Utilities.Extensions;

namespace LootingBots.Utilities;

public class MoveMultipleResult : IOperationResult
{
    private readonly List<OperationResult<MoveResult>> _moveOperations;
    private readonly LootingTransactionController _controller;

    public float NetWorthDelta { get; private set; }

    public int Count
    {
        get { return _moveOperations.Count; }
    }

    public MoveMultipleResult(List<OperationResult<MoveResult>> operations, LootingTransactionController controller, float netWorthDelta)
    {
        _moveOperations = UnityEngine.Pool.ListPool<OperationResult<MoveResult>>.Get();
        for (var i = 0; i < operations.Count; i++)
        {
            _moveOperations.Add(operations[i]);
        }
        _controller = controller;
        NetWorthDelta = netWorthDelta;
    }

    public async Task<IResult> ExecuteAsync()
    {
        try
        {
            foreach (var moveOperation in _moveOperations)
            {
                var networkResult = await _controller.TryRunNetworkTransactionWithTimeoutAsync(moveOperation);
                if (networkResult.Failed)
                {
                    return new FailedResult(networkResult.Error);
                }
            }

            return SuccessfulResult.New;
        }
        finally
        {
            UnityEngine.Pool.ListPool<OperationResult<MoveResult>>.Release(_moveOperations);
        }
    }

    public bool CanExecute(ItemController itemController)
    {
        foreach (var moveOperation in _moveOperations)
        {
            if (!moveOperation.Value.CanExecute(itemController))
            {
                return false;
            }
        }
        return true;
    }

    public void RaiseEvents(IItemOwner controller, CommandStatus status)
    {
        foreach (var moveOperation in _moveOperations)
        {
            moveOperation.Value.RaiseEvents(controller, status);
        }
    }

    public void RollBack()
    {
        _moveOperations.SafeRollBack();
    }
}
