/// <summary>
/// The system clipboard, which the Party page's Copy buttons write to, so tests read what was copied.
/// </summary>
public interface IClipboard
{
    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard.
    /// </summary>
    void Copy(string text);
}
