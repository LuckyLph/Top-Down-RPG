/// <summary>Reads and writes the save file's text, so save logic can be tested without touching the disk.</summary>
public interface ISaveStore
{
    /// <summary>
    /// Reads the stored text; returns false, with null <paramref name="contents"/>, when nothing is stored or it
    /// cannot be read.
    /// </summary>
    bool TryRead(out string contents);

    /// <summary>
    /// Replaces the stored text with <paramref name="contents"/>; returns whether it was written.
    /// </summary>
    bool Write(string contents);

    /// <summary>
    /// Keeps a copy of what is stored, whether it can be read or not, beside it under a name no copy has yet, before
    /// something replaces it: <paramref name="copy"/> names it, or is null when nothing is stored. Returns false when
    /// the copy could not be made.
    /// </summary>
    bool KeepCopy(out string copy);
}
