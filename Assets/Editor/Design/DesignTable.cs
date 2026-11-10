/// <summary>
/// One file of <c>Docs/Generated</c> as <see cref="DesignTables"/> writes it: its name and its Markdown.
/// </summary>
public readonly struct DesignTable
{
    /// <summary>
    /// Creates the table <paramref name="fileName"/> holding <paramref name="markdown"/>.
    /// </summary>
    public DesignTable(string fileName, string markdown)
    {
        FileName = fileName;
        Markdown = markdown;
    }

    public string FileName { get; }
    public string Markdown { get; }
}
