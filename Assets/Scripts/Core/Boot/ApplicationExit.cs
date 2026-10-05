/// <summary>
/// Closes the application, or leaves Play Mode in the editor.
/// </summary>
public static class ApplicationExit
{
    /// <summary>
    /// Quits the player build; in the editor it stops Play Mode instead.
    /// </summary>
    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }
}
