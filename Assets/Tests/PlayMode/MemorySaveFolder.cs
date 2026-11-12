using System;
using System.Collections.Generic;

/// <summary>
/// In-memory <see cref="ISaveFolder"/> for tests: its files by name, each counting its writes and keeping copies as
/// <see cref="MemorySaveStore"/> does, the earlier builds' single save file (<see cref="LegacyStore"/>) and what retiring
/// it moved aside, the name and text of the file written last, the copy a delete kept of each file, and a way to seed
/// a session's save.
/// </summary>
public sealed class MemorySaveFolder : ISaveFolder
{
    private readonly Dictionary<string, File> files = new();
    private readonly Dictionary<string, string> deleted = new();
    private int seeded;

    public MemorySaveStore LegacyStore { get; } = new();
    public ISaveStore Legacy => LegacyStore;
    public string RetiredLegacy { get; private set; }
    public string LastWrittenName { get; private set; }
    public int WriteCount { get; private set; }

    /// <summary>
    /// The text of the file written last; set, a session's save seeded under a name of its own (see
    /// <see cref="Seed(string)"/>).
    /// </summary>
    public string Contents
    {
        get => LastWrittenName != null ? files[LastWrittenName].Contents : null;
        set => Seed(value);
    }

    /// <summary>
    /// The names of the files holding something, sorted.
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        List<string> names = new();
        foreach (KeyValuePair<string, File> file in files)
        {
            if (file.Value.Contents != null)
            {
                names.Add(file.Key);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// The file named <paramref name="name"/>, made empty on first use.
    /// </summary>
    public ISaveStore Open(string name)
    {
        return FileNamed(name);
    }

    /// <summary>
    /// The text of the file named <paramref name="name"/>; null when it holds nothing.
    /// </summary>
    public string Read(string name)
    {
        return files.TryGetValue(name, out File file) ? file.Contents : null;
    }

    /// <summary>
    /// The copies kept of the file named <paramref name="name"/> before something wrote over it.
    /// </summary>
    public IReadOnlyList<string> KeptCopiesOf(string name)
    {
        return files.TryGetValue(name, out File file) ? file.KeptCopies : Array.Empty<string>();
    }

    /// <summary>
    /// Takes the text out of the file named <paramref name="name"/>, keeping it as that file's one deleted copy
    /// (<see cref="DeletedCopyOf"/>), which replaces the copy an earlier delete kept; false when it holds nothing.
    /// </summary>
    public bool Delete(string name)
    {
        if (!files.TryGetValue(name, out File file) || file.Contents == null)
        {
            return false;
        }

        deleted[name] = file.Contents;
        file.Contents = null;
        return true;
    }

    /// <summary>
    /// The text the last delete of the file named <paramref name="name"/> kept, as <c>&lt;name&gt;.bak</c> would hold it;
    /// null when it was never deleted.
    /// </summary>
    public string DeletedCopyOf(string name)
    {
        return deleted.TryGetValue(name, out string text) ? text : null;
    }

    /// <summary>
    /// Moves the legacy file's text to <see cref="RetiredLegacy"/>, as renaming it would; nothing to move when it holds
    /// nothing. Always succeeds.
    /// </summary>
    public bool RetireLegacy(out string moved)
    {
        moved = null;
        if (LegacyStore.Contents == null)
        {
            return true;
        }

        RetiredLegacy = LegacyStore.Contents;
        LegacyStore.Contents = null;
        moved = FileSaveStore.DefaultFileName + FileSaveFolder.ImportedSuffix;
        return true;
    }

    /// <summary>
    /// Puts <paramref name="json"/> in the folder as a session's save, under <c>session-seeded-N.json</c>, as written
    /// before the test began: it counts as no write.
    /// </summary>
    public string Seed(string json)
    {
        seeded++;
        string name = $"{GameSave.SessionPrefix}seeded-{seeded}{FileSaveFolder.Extension}";
        FileNamed(name).Contents = json;
        LastWrittenName = name;
        return name;
    }

    /// <summary>
    /// Puts <paramref name="data"/> in the folder under the name <see cref="GameSave"/> writes it to, as written before
    /// the test began.
    /// </summary>
    public string Seed(GameSaveData data)
    {
        string name = GameSave.FileNameOf(data);
        FileNamed(name).Contents = data.ToJson();
        LastWrittenName = name;
        return name;
    }

    /// <summary>
    /// The file named <paramref name="name"/>, made on first use.
    /// </summary>
    private File FileNamed(string name)
    {
        if (!files.TryGetValue(name, out File file))
        {
            file = new File(this, name);
            files.Add(name, file);
        }

        return file;
    }

    /// <summary>One file of the folder, telling it of each write.</summary>
    private sealed class File : ISaveStore
    {
        private readonly MemorySaveFolder folder;
        private readonly string name;

        /// <summary>
        /// Creates the empty file <paramref name="fileName"/> of <paramref name="owner"/>.
        /// </summary>
        public File(MemorySaveFolder owner, string fileName)
        {
            folder = owner;
            name = fileName;
        }

        public string Contents { get; set; }
        public List<string> KeptCopies { get; } = new();

        /// <summary>
        /// Returns the stored text; fails when nothing has been stored.
        /// </summary>
        public bool TryRead(out string contents)
        {
            contents = Contents;
            return contents != null;
        }

        /// <summary>
        /// Replaces the stored text, counts the write in its folder and always succeeds.
        /// </summary>
        public bool Write(string contents)
        {
            Contents = contents;
            folder.WriteCount++;
            folder.LastWrittenName = name;
            return true;
        }

        /// <summary>
        /// Keeps the stored text in <see cref="KeptCopies"/>, naming the copy by its number; no copy while nothing is
        /// stored. Always succeeds.
        /// </summary>
        public bool KeepCopy(out string copy)
        {
            copy = null;
            if (Contents != null)
            {
                KeptCopies.Add(Contents);
                copy = $"{name} copy {KeptCopies.Count}";
            }

            return true;
        }
    }
}
