using UnityEngine;

namespace LootingBots.Utilities;

public sealed class ColliderDistanceComparer(Vector3 referencePosition) : IComparer<Collider>
{
    public static readonly ColliderDistanceComparer Instance = new(Vector3.zero);

    private Vector3 _referencePosition = referencePosition;

    public void SetReferencePosition(Vector3 referencePosition)
    {
        _referencePosition = referencePosition;
    }

    public int Compare(Collider x, Collider y)
    {
        var distX = Vector3.SqrMagnitude(x.bounds.center - _referencePosition);
        var distY = Vector3.SqrMagnitude(y.bounds.center - _referencePosition);
        return distX.CompareTo(distY);
    }
}
