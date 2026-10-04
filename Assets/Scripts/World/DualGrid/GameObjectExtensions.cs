using UnityEngine;

public static class GameObjectExtensions
{
    /// <summary>
    /// Returns the first component found in immediate parent of <paramref name="gameObject"/>.
    /// </summary>
    /// <remarks>
    /// It will not return a component found in the <paramref name="gameObject"/>'s game object.
    /// </remarks>
    public static T GetComponentInImmediateParent<T>(this GameObject gameObject) where T : Component
    {
        Transform parent = gameObject.transform.parent;
        return parent == null ? null : parent.GetComponent<T>();
    }
}
