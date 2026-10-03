using Diz.LanguageExtensions;
using EFT.InventoryLogic;

namespace LootingBots.Utilities;

public static class OperationsExtensions
{
    /// <summary>
    /// <see cref="InventoryOperationExtensions.RollBack{T}(IReadOnlyList{OperationResult{T}})"/> with Value null check.
    /// </summary>
    public static void SafeRollBack<T>(this IReadOnlyList<OperationResult<T>> operations)
        where T : IOperationResult
    {
        for (var i = operations.Count - 1; i >= 0; i--)
        {
            operations[i].Value?.RollBack();
        }
    }
}
