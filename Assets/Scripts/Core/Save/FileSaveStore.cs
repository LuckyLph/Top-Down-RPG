using System;
using System.IO;
using UnityEngine;

/// <summary>Stores the save as a text file, writing a temporary file first and swapping it in so a crash never leaves a half-written save.</summary>
public sealed class FileSaveStore : ISaveStore
{
    public const string DefaultFileName = "save.json";

    private readonly string filePath;
    private readonly string tempPath;

    public FileSaveStore(string filePath)
    {
        this.filePath = filePath;
        tempPath = filePath + ".tmp";
    }

    public string FilePath => filePath;

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
