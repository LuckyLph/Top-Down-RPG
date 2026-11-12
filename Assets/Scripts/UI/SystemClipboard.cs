using UnityEngine;

/// <summary>
/// The operating system's clipboard, through <see cref="GUIUtility.systemCopyBuffer"/>.
/// </summary>
public sealed class SystemClipboard : IClipboard
{
    /// <summary>
    /// Puts <paramref name="text"/> on the system clipboard.
    /// </summary>
    public void Copy(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }
}
