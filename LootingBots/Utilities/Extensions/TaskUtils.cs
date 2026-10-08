using Cysharp.Threading.Tasks;
using EFT.InventoryLogic;

#pragma warning disable VSTHRD003

namespace LootingBots.Utilities.Extensions;

public static class TaskExtensions
{
    public static async UniTask<bool> ReleaseOnFinishAsync<T>(this UniTask<bool> task, List<T> list)
        where T : Item
    {
        try
        {
            return await task;
        }
        finally
        {
            UnityEngine.Pool.ListPool<T>.Release(list);
        }
    }
}
