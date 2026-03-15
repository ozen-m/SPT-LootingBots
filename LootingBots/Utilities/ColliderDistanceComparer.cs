using UnityEngine;

namespace LootingBots.Utilities;

public class ColliderDistanceComparer(Vector3 referencePosition) : IComparer<Collider>
{
    public int Compare(Collider x, Collider y)
    {
        var distX = Vector3.SqrMagnitude(x.bounds.center - referencePosition);
        var distY = Vector3.SqrMagnitude(y.bounds.center - referencePosition);
        return distX.CompareTo(distY);
    }
}
