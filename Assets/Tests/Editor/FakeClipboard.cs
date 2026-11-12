/// <summary>
/// <see cref="IClipboard"/> that keeps what was copied last.
/// </summary>
public sealed class FakeClipboard : IClipboard
{
    public string Text { get; private set; }

    /// <summary>
    /// Keeps <paramref name="text"/>.
    /// </summary>
    public void Copy(string text)
    {
        Text = text;
    }
}
