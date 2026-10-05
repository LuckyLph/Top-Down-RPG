#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// Editor only: carries the scenes that were open when Play was pressed across the domain reload into Play Mode, in
/// <see cref="SessionState"/>, so <see cref="BootFlow"/> can boot straight into them.
/// </summary>
public static class EditorBootRequest
{
    private const string ScenesKey = "TopDownRPG.EditorBootRequest.Scenes";
    private const char Separator = ';';

    /// <summary>
    /// Stores <paramref name="scenePaths"/> for the next boot, or clears the request when there are none.
    /// </summary>
    public static void Set(string[] scenePaths)
    {
        if (scenePaths == null || scenePaths.Length == 0)
        {
            Clear();
            return;
        }

        SessionState.SetString(ScenesKey, string.Join(Separator, scenePaths));
    }

    /// <summary>
    /// Drops any stored request, so the next boot shows the main menu.
    /// </summary>
    public static void Clear()
    {
        SessionState.EraseString(ScenesKey);
    }

    /// <summary>
    /// Reads and clears the stored request, so it applies to one boot only. Returns false, with an empty array, when
    /// none was stored.
    /// </summary>
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
