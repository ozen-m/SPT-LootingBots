using UnityEngine;

namespace LootingBots.Utilities;

public class TimeoutRunner : MonoBehaviourSingleton<TimeoutRunner>
{
    private readonly List<TimeoutController> _timeouts = [];

    public static TimeoutRunner Init()
    {
        return Instantiated ? Instance : new GameObject(nameof(TimeoutRunner)).AddComponent<TimeoutRunner>();
    }

    public void Update()
    {
        var deltaTime = Time.deltaTime;

        for (var i = 0; i < _timeouts.Count; i++)
        {
            if (_timeouts[i].Run(deltaTime))
            {
                continue;
            }

            Remove(i);
        }
    }

    public void Add(TimeoutController timeout)
    {
        _timeouts.Add(timeout);
    }

    private void Remove(int index)
    {
        var last = _timeouts.Count - 1;
        _timeouts[index] = _timeouts[last];
        _timeouts.RemoveAt(last);
    }

    public override void OnDestroy()
    {
        foreach (var timeout in _timeouts)
        {
            timeout.Dispose();
        }
        _timeouts.Clear();

        base.OnDestroy();
    }
}
