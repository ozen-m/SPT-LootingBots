using EFT.InventoryLogic;

#pragma warning disable VSTHRD003

namespace LootingBots.Utilities.Extensions;

public static class TaskExtensions
{
    public static async Task<bool> ReleaseOnFinishAsync<T>(this Task<bool> task, List<T> list)
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
