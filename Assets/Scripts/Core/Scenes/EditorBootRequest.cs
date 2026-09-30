#if UNITY_EDITOR
using System;
using UnityEditor;

public static class EditorBootRequest
{
    private const string ScenesKey = "TopDownRPG.EditorBootRequest.Scenes";
    private const char Separator = ';';

    public static void Set(string[] scenePaths)
    {
        if (scenePaths == null || scenePaths.Length == 0)
        {
            Clear();
            return;
        }

        SessionState.SetString(ScenesKey, string.Join(Separator, scenePaths));
    }

    public static void Clear()
    {
        SessionState.EraseString(ScenesKey);
    }

    public static bool TryConsume(out string[] scenePaths)
    {
        string stored = SessionState.GetString(ScenesKey, string.Empty);
        Clear();

        scenePaths = string.IsNullOrEmpty(stored)
            ? Array.Empty<string>()
            : stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        return scenePaths.Length > 0;
    }
}
#endif
