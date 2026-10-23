using System;
using System.IO;
using UnityEngine;

/// <summary>Stores the save as a text file, writing a temporary file first and swapping it in so a crash never leaves a half-written save.</summary>
public sealed class FileSaveStore : ISaveStore
{
    public const string DefaultFileName = "save.json";

    private readonly string filePath;
    private readonly string tempPath;

    /// <summary>
    /// Creates a store for <paramref name="filePath"/>, staging writes in a <c>.tmp</c> file beside it.
    /// </summary>
    public FileSaveStore(string filePath)
    {
        this.filePath = filePath;
        tempPath = filePath + ".tmp";
    }

    public string FilePath => filePath;

    /// <summary>
    /// Reads the whole file; returns false when it does not exist, or logs an error and returns false when it cannot
    /// be read.
    /// </summary>
    public bool TryRead(out string contents)
    {
        contents = null;
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            contents = File.ReadAllText(filePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"Could not read the save file '{filePath}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Copies the file to <c>.bak</c> beside it, or <c>.bak2</c>, <c>.bak3</c> and on when those exist; true with no copy
    /// when there is no file. Logs an error and returns false when the disk refuses.
    /// </summary>
    public bool KeepCopy(out string copy)
    {
        copy = null;
        if (!File.Exists(filePath))
        {
            return true;
        }

        string candidate = filePath + ".bak";
        for (int number = 2; File.Exists(candidate); number++)
        {
            candidate = $"{filePath}.bak{number}";
        }

        try
        {
            File.Copy(filePath, candidate);
            copy = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"Could not keep a copy of the save file '{filePath}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates the folder if needed, writes <paramref name="contents"/> to the temporary file and swaps it in for the
    /// save. Logs an error and returns false when the disk refuses.
    /// </summary>
    public bool Write(string contents)
    {
        try
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, contents);
            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"Could not write the save file '{filePath}': {exception.Message}");
            return false;
        }
    }
}
