using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The save files as a folder on disk (<c>Saves</c> in the persistent data folder, a Multiplayer Play Mode virtual
/// player's own, see <see cref="InstanceFiles"/>), each file a <see cref="FileSaveStore"/>, and the single
/// <c>save.json</c> of earlier builds beside it, renamed <c>save.json.imported</c> once imported.
/// </summary>
public sealed class FileSaveFolder : ISaveFolder
{
    public const string DefaultFolderName = "Saves";
    public const string Extension = ".json";
    public const string ImportedSuffix = ".imported";

    private readonly string folderPath;
    private readonly string legacyPath;

    /// <summary>
    /// Creates the folder at <paramref name="folderPath"/> (made at its first write), with the earlier builds' save file
    /// at <paramref name="legacyPath"/>.
    /// </summary>
    public FileSaveFolder(string folderPath, string legacyPath)
    {
        this.folderPath = folderPath;
        this.legacyPath = legacyPath;
        Legacy = new FileSaveStore(legacyPath);
    }

    public string FolderPath => folderPath;
    public ISaveStore Legacy { get; }

    /// <summary>
    /// The names of the <c>.json</c> files in the folder, sorted; none when it does not exist or cannot be read (an
    /// error logged).
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        List<string> names = new();
        if (!Directory.Exists(folderPath))
        {
            return names;
        }

        try
        {
            foreach (string path in Directory.GetFiles(folderPath, "*" + Extension))
            {
                names.Add(Path.GetFileName(path));
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"Could not list the save folder '{folderPath}': {exception.Message}");
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// The store of <paramref name="name"/> in the folder.
    /// </summary>
    public ISaveStore Open(string name)
    {
        return new FileSaveStore(Path.Combine(folderPath, name));
    }

    /// <summary>
    /// Renames the legacy file to <c>save.json.imported</c> (numbered after the first); true with no name when it is
    /// gone already. Logs an error and returns false when the disk refuses.
    /// </summary>
    public bool RetireLegacy(out string moved)
    {
        moved = null;
        if (!File.Exists(legacyPath))
        {
            return true;
        }

        string candidate = legacyPath + ImportedSuffix;
        for (int number = 2; File.Exists(candidate); number++)
        {
            candidate = $"{legacyPath}{ImportedSuffix}{number}";
        }

        try
        {
            File.Move(legacyPath, candidate);
            moved = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"Could not rename the imported save file '{legacyPath}': {exception.Message}");
            return false;
        }
    }
}
