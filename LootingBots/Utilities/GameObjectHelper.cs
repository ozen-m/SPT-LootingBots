using UnityEngine;

namespace LootingBots.Utilities;

/// <summary>
/// Debug spheres from DrakiaXYZ Waypoints.
/// </summary>
/// <seealso href="https://github.com/DrakiaXYZ/SPT-Waypoints/blob/master/Helpers/GameObjectHelper.cs"/>
public static class GameObjectHelper
{
    public static GameObject DrawSphere(Vector3 position, float size, Color color)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.GetComponent<Renderer>().material.color = color;
        sphere.GetComponent<Collider>().enabled = false;
        sphere.transform.position = position;
        sphere.transform.localScale = new Vector3(size, size, size);

        return sphere;
    }
}
