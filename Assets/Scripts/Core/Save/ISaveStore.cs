/// <summary>Reads and writes the save file's text, so save logic can be tested without touching the disk.</summary>
public interface ISaveStore
{
    bool TryRead(out string contents);

    bool Write(string contents);
}
