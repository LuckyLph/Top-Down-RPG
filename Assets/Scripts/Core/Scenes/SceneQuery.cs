using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneQuery
{
    private static readonly List<GameObject> RootBuffer = new();

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
