using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds components inside one scene, inactive objects included, for composition roots that wire a scene's own
/// objects into its scope. Main-thread only: it shares one root-object buffer.
/// </summary>
public static class SceneQuery
{
    private static readonly List<GameObject> RootBuffer = new();

    /// <summary>
    /// The first <typeparamref name="T"/> found under <paramref name="scene"/>'s root objects, in hierarchy order, or
    /// null when there is none.
    /// </summary>
    public static T FindFirst<T>(Scene scene) where T : Component
    {
        scene.GetRootGameObjects(RootBuffer);
        try
        {
            for (int i = 0; i < RootBuffer.Count; i++)
            {
                T found = RootBuffer[i].GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
        finally
        {
            RootBuffer.Clear();
        }
    }

    /// <summary>
    /// Every <typeparamref name="T"/> under <paramref name="scene"/>'s root objects, in hierarchy order; an empty
    /// array when there is none.
    /// </summary>
    public static T[] FindAll<T>(Scene scene) where T : Component
    {
        List<T> results = new();
        scene.GetRootGameObjects(RootBuffer);
        try
        {
            for (int i = 0; i < RootBuffer.Count; i++)
            {
                results.AddRange(RootBuffer[i].GetComponentsInChildren<T>(true));
            }

            return results.ToArray();
        }
        finally
        {
            RootBuffer.Clear();
        }
    }
}
