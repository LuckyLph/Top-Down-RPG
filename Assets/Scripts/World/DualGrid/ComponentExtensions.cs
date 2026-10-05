using UnityEngine;

/// <summary>
/// Component lookups limited to one level of the hierarchy, used to pair a dual-grid data tilemap with its render
/// tilemap child.
/// </summary>
public static class ComponentExtensions
{
    /// <summary>
    /// Returns the first component found in immediate children of <paramref name="parent"/>.
    /// </summary>
    /// <remarks>
    /// It will not return a component found in the <paramref name="parent"/>'s game object.
    /// </remarks>
    public static T GetComponentInImmediateChildren<T>(this Component parent) where T : Component
    {
        foreach (Transform child in parent.transform)
        {
            T component = child.GetComponent<T>();
            if (component != null && component.transform != parent.transform)
            {
                return component;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the first component found in immediate parent of <paramref name="component"/>.
    /// </summary>
    /// <remarks>
    /// It will not return a component found in the <paramref name="component"/>'s game object.
    /// </remarks>
    public static T GetComponentInImmediateParent<T>(this Component component) where T : Component
    {
        Transform parent = component.transform.parent;
        return parent == null ? null : parent.GetComponent<T>();
    }
}
