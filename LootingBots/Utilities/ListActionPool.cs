using LootingBots.Actions;
using UnityEngine.Pool;

namespace LootingBots.Utilities;

public static class ListActionPool
{
    private static readonly ObjectPool<List<LootingAction>> _pool = new(
        () => [],
        null,
        OnRelease,
        LogOnDestroyInstance,
        true,
        2,
        32
    );

    public static List<LootingAction> Rent()
    {
        return _pool.Get();
    }

    public static void Return(List<LootingAction> list)
    {
        _pool.Release(list);
    }

    private static void OnRelease(List<LootingAction> lootingAction)
    {
        foreach (var action in lootingAction)
        {
            action.Return();
        }
        lootingAction.Clear();
    }

    public static void LogOnDestroyInstance<T>(T instance)
    {
        var log = LootingBots.LootLog;
        if (log.DebugEnabled)
        {
            LootingBots.LootLog.LogError($"Destroyed instance of {instance.GetType().FullName}");
        }
    }
}
