using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The names of the files this running instance keeps its save and settings in. Multiplayer Play Mode's virtual
/// players share the main editor's data folder, so in the editor a virtual player, launched with <c>-name Player2</c>
/// to <c>-name Player4</c>, keeps files of its own (<c>save.Player2.json</c>, <c>settings.Player2.json</c>) and so its
/// own player id, key bindings and offline save, as a separate install would. The main editor and builds use the plain
/// names.
/// </summary>
public static class InstanceFiles
{
    private const string NameArgument = "-name";
    private const string PlayerPrefix = "Player";
    private const string MainEditorPlayer = "Player1";

    /// <summary>
    /// The name this instance uses for <paramref name="fileName"/>: the plain name, or in an editor launched as a
    /// virtual player, the name with the player inserted before the extension.
    /// </summary>
    public static string NameFor(string fileName)
    {
#if UNITY_EDITOR
        return NameFor(fileName, Environment.GetCommandLineArgs());
#else
        return fileName;
#endif
    }

    /// <summary>
    /// The name for <paramref name="fileName"/> given the instance's <paramref name="commandLine"/>: with the virtual
    /// player's name (<c>Player</c> and a number other than 1, after <c>-name</c>) before the extension, else unchanged.
    /// </summary>
    public static string NameFor(string fileName, IReadOnlyList<string> commandLine)
    {
        string player = VirtualPlayerOf(commandLine);
        return player == null ? fileName : $"{Path.GetFileNameWithoutExtension(fileName)}.{player}{Path.GetExtension(fileName)}";
    }

    /// <summary>
    /// The virtual player named after <c>-name</c> in <paramref name="commandLine"/>; null when there is none, when it
    /// is the main editor's <c>Player1</c>, or when the name is not <c>Player</c> followed by digits.
    /// </summary>
    private static string VirtualPlayerOf(IReadOnlyList<string> commandLine)
    {
        if (commandLine == null)
        {
            return null;
        }

        for (int i = 0; i < commandLine.Count - 1; i++)
        {
            if (commandLine[i] != NameArgument)
            {
                continue;
            }

            string name = commandLine[i + 1];
            return IsVirtualPlayer(name) ? name : null;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is <c>Player</c> followed by digits, other than <c>Player1</c>.
    /// </summary>
    private static bool IsVirtualPlayer(string name)
    {
        if (string.IsNullOrEmpty(name) || name == MainEditorPlayer || name.Length <= PlayerPrefix.Length
            || !name.StartsWith(PlayerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = PlayerPrefix.Length; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
            {
                return false;
            }
        }

        return true;
    }
}
