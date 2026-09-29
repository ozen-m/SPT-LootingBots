using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using LootingBots.Components;
using UnityEngine;
using UnityEngine.Pool;
using Grid = EFT.InventoryLogic.Grid;

namespace LootingBots.Utilities;

public class MoveMultipleResult : IOperationResult
{
    private readonly List<MoveResult> _moveResults;
    private readonly LootingTransactionController _transactionController;

    public float NetWorthDelta { get; private set; }

    public int Count
    {
        get { return _moveResults.Count; }
    }

    public MoveMultipleResult(List<MoveResult> moveResults, LootingTransactionController transactionController, float newItemsValue)
    {
        _moveResults = UnityEngine.Pool.ListPool<MoveResult>.Get();
        for (var i = 0; i < moveResults.Count; i++)
        {
            _moveResults.Add(moveResults[i]);
        }
        _transactionController = transactionController;
        NetWorthDelta = newItemsValue;
    }

    public async Task<IResult> ExecuteAsync()
    {
        try
        {
            foreach (var moveResult in _moveResults)
            {
                var operationResult = moveResult.Execute();
                if (operationResult.Failed)
                {
                    return new FailedResult(operationResult.Error!.ToString());
                }

                await _transactionController.TryRunNetworkTransactionWithTimeoutAsync(operationResult);
            }

            return SuccessfulResult.New;
        }
        finally
        {
            UnityEngine.Pool.ListPool<MoveResult>.Release(_moveResults);
        }
    }

    public bool CanExecute(ItemController itemController)
    {
        foreach (var moveResult in _moveResults)
        {
            if (!moveResult.CanExecute(itemController))
            {
                return false;
            }
        }
        return true;
    }

    public void RaiseEvents(IItemOwner controller, CommandStatus status)
    {
        foreach (var moveResult in _moveResults)
        {
            moveResult.RaiseEvents(controller, status);
        }
    }

    public void RollBack()
    {
        foreach (var moveResult in _moveResults)
        {
            moveResult.RollBack();
        }
    }
}

public static class ItemManipulatorEx
{
    /// <summary>
    /// This method tries to place <paramref name="container"/> into a suitable grid from the backpack slot.
    /// Conflicting items which block the placement of the container inside that grid is moved to the container,
    /// then is checked if it can now fit inside that grid.
    /// </summary>
    /// <param name="container">Item to placed inside any grid in the backpack</param>
    public static OperationResult<MoveMultipleResult> TryFillContainerAndPickUp(
        SearchableItem container,
        InventoryController controller,
        LootingTransactionController transaction,
        BotLog log = null
    )
    {
        if (controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is not SearchableItem backpack)
        {
            return NoTargetError.New(container);
        }

        // Get all grids of the backpack
        using var pooledGrids = UnityEngine.Pool.ListPool<Grid>.Get(out var grids);
        backpack.GetPrioritizedGridsForLootNonAlloc(null, grids);

        using var pooledItems = UnityEngine.Pool.ListPool<GridItemRect>.Get(out var gridItems);
        using var pooledToMove = UnityEngine.Pool.ListPool<GridItemRect>.Get(out var itemsToMove);
        using var pooledResults = UnityEngine.Pool.ListPool<MoveResult>.Get(out var operations);
        using var pooledFailed = HashSetPool<Item>.Get(out var failedItems);

        var cellSize = container.CalculateCellSize();
        var containerSize = cellSize.X * cellSize.Y;

        for (var i = grids.Count - 1; i >= 0; i--)
        {
            var grid = grids[i];
            var gridWidth = grid._gridWidth;
            var gridHeight = grid._gridHeight;
            var gridSize = gridWidth * gridHeight;

            // Skip this grid if the container cannot fit inside it
            if (containerSize > gridSize)
            {
                continue;
            }

            // Calculate the size of each contained item in this grid,
            // this is used to check if an item conflicts with the placement of the new container.
            failedItems.Clear();
            gridItems.Clear();
            foreach (var (item, location) in grid._itemCollection.Items)
            {
                var itemSize = item.CalculateRotatedSize(location.r);
                gridItems.Add(new GridItemRect(item, new IntRect(location.x, location.y, itemSize.X, itemSize.Y)));
            }

            // Try both rotations, starting vertical
            for (var rotation = 1; rotation >= 0; rotation--)
            {
                // Skip rotation if the container is a square
                if (rotation == 0 && cellSize.X == cellSize.Y)
                {
                    break;
                }

                var rotatedSize = rotation == 0 ? cellSize : new IntVec2(cellSize.Y, cellSize.X);

                // Skip this rotation if the container cannot fit when rotated
                if (rotatedSize.X > gridWidth || rotatedSize.Y > gridHeight)
                {
                    continue;
                }

                // Walk every possible location for the container
                for (var x = 0; x <= gridWidth - rotatedSize.X; x++)
                {
                    for (var y = 0; y <= gridHeight - rotatedSize.Y; y++)
                    {
                        var rect = new IntRect(x, y, rotatedSize.X, rotatedSize.Y);

                        itemsToMove.Clear();

                        // Get all the items blocking this candidate location,
                        // these will be moved to the container.
                        var isBlocked = false;
                        foreach (var gridItem in gridItems)
                        {
                            if (!Grid.IsConflicting(gridItem.Rect, rect))
                            {
                                continue;
                            }
                            if (failedItems.Contains(gridItem.Item))
                            {
                                // Since we can't move this item, jump past the item's height
                                y = Mathf.Max(y, gridItem.Rect.Y + gridItem.Rect.Height - 1);
                                isBlocked = true;
                                break;
                            }

                            itemsToMove.Add(gridItem);
                        }

                        if (isBlocked)
                        {
                            continue;
                        }

                        operations.Clear();

                        // Move all conflicting items into the container
                        var conflictingItemsMoved = true;
                        foreach (var toMove in itemsToMove)
                        {
                            var location = container.FindGridToPickUpLootNonAlloc(toMove.Item);
                            if (location is null)
                            {
                                // Can't move to the container, or no longer has space,
                                // mark it as failed and jump past its height.
                                failedItems.Add(toMove.Item);
                                y = Mathf.Max(y, toMove.Rect.Y + toMove.Rect.Height - 1);
                                conflictingItemsMoved = false;
                                break;
                            }

                            var moveOperation = ItemManipulator.Move(toMove.Item, location, controller, false);
                            if (moveOperation.Failed)
                            {
                                failedItems.Add(toMove.Item);
                                conflictingItemsMoved = false;
                                y = Mathf.Max(y, toMove.Rect.Y + toMove.Rect.Height - 1);
                                break;
                            }

                            operations.Add(moveOperation.Value);
                        }

                        // Try placing the container into the grid, if we can now place it into the grid, we've successfully finished
                        var success = false;
                        if (conflictingItemsMoved && grid.TryFindLocationForItem(container, out var gridAddress))
                        {
                            var moveOperation = ItemManipulator.Move(container, gridAddress, controller, false);
                            if (moveOperation.Succeeded)
                            {
                                operations.Add(moveOperation.Value);
                                success = true;
                            }
                        }

                        // Rollback all operations regardless of success
                        for (var j = operations.Count - 1; j >= 0; j--)
                        {
                            operations[j].RollBack();
                        }

                        if (success)
                        {
                            return new MoveMultipleResult(operations, transaction, backpack.GetAllContainedItemsValue(log));
                        }
                    }
                }
            }
        }

        return NoLocationError.New(backpack, container);
    }

    public class NoTargetError : Error
    {
        private static readonly NoTargetError _new = new();

        private Item _container;

        private NoTargetError() { }

        public static NoTargetError New(Item itemToLoot)
        {
            _new._container = itemToLoot;
            return _new;
        }

        public override string ToString()
        {
            return $"Does not have a target to move items from into container [{_container.LocalizedName()}]";
        }
    }

    public class NoLocationError : Error
    {
        private static readonly NoLocationError _new = new();

        private Item _backpack;
        private Item _container;

        private NoLocationError() { }

        public static NoLocationError New(Item source, Item container)
        {
            _new._backpack = source;
            _new._container = container;
            return _new;
        }

        public override string ToString()
        {
            return $"Could not find a location to move container [{_container.LocalizedName()}] to backpack [{_backpack.LocalizedName()}]";
        }
    }

    private readonly struct GridItemRect(Item item, IntRect rect)
    {
        public readonly Item Item = item;
        public readonly IntRect Rect = rect;
    }
}
