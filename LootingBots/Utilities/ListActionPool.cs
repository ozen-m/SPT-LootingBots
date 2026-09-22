using LootingBots.Actions;
using UnityEngine.Pool;

namespace LootingBots.Utilities;

public static class ListActionPool
{
    private static readonly UnityEngine.Pool.ObjectPool<List<LootingAction>> _pool = new(
        () => [],
        null,
        OnRelease,
        LogOnDestroyInstance,
        false,
        2,
        32
    );

    /// <summary>
    /// Get a <see cref="List{LootingAction}"/> from the pool. If the pool is empty then a new instance will be created.
    /// </summary>
    /// <returns>A <see cref="List{LootingAction}"/> or a new instance if the pool is empty</returns>
    public static List<LootingAction> Get()
    {
        return _pool.Get();
    }

    /// <summary>
    /// Get a <see cref="List{LootingAction}"/> from the pool. If the pool is empty then a new instance will be created.
    /// </summary>
    /// <param name="instance">Out parameter that will contain a reference to an instance from the pool.</param>
    /// <returns>A <see cref="PooledObject{List{LootingAction}}"/> that will return the instance back to the pool when its Dispose method is called.</returns>
    public static PooledObject<List<LootingAction>> Get(out List<LootingAction> instance)
    {
        instance = _pool.Get();
        return new PooledObject<List<LootingAction>>(instance, _pool);
    }

    /// <summary>
    /// Returns the instance back to the pool.
    /// </summary>
    /// <param name="instance">The instance to return to the pool.</param>
    public static void Release(List<LootingAction> instance)
    {
        _pool.Release(instance);
    }

    /// <summary>
    /// Resets all the elements in the list and clears the list.
    /// Used when reusing the list before returning.
    /// </summary>
    /// <seealso cref="OnRelease"/>
    public static void Reset(this List<LootingAction> instance)
    {
        OnRelease(instance);
    }

    /// <summary>
    /// Return each LootingAction on the list and then clear the list
    /// </summary>
    private static void OnRelease(List<LootingAction> instance)
    {
        foreach (var action in instance)
        {
            action.Return();
        }
        instance.Clear();
    }

    public static void LogOnDestroyInstance<T>(T value)
    {
        var log = LootingBots.LootLog;
        if (log.DebugEnabled)
        {
            log.LogError($"Destroyed instance of {value.GetType().FullName}");
        }
    }
}
