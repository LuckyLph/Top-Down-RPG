using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds components inside one scene, inactive objects included, for composition roots that wire a scene's own
/// objects into its scope. Each call reads the scene's root objects into an array of its own, so it shares no state.
/// </summary>
public static class SceneQuery
{
    /// <summary>
    /// The first <typeparamref name="T"/> found under <paramref name="scene"/>'s root objects, in hierarchy order, or
    /// null when there is none.
    /// </summary>
    public static T FindFirst<T>(Scene scene) where T : Component
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            T found = roots[i].GetComponentInChildren<T>(true);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Every <typeparamref name="T"/> under <paramref name="scene"/>'s root objects, in hierarchy order; an empty
    /// array when there is none.
    /// </summary>
    public static T[] FindAll<T>(Scene scene) where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        }

        return results.ToArray();
    }
}
