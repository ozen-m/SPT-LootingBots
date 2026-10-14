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

    public static GameObject[] CreateDebugSpheres()
    {
        var debugSpheres = new GameObject[3];
        debugSpheres[0] = DrawSphere(Vector3.zero, 0.35f, Color.red); // center
        debugSpheres[1] = DrawSphere(Vector3.zero, 0.35f, Color.green); // pointNearbyContainer
        debugSpheres[2] = DrawSphere(Vector3.zero, 0.35f, Color.blue); // destination
        return debugSpheres;
    }
}
