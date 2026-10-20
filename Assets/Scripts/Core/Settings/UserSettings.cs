using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The local machine's preferences, read once when created and written on every change, and this install's player id
/// (Docs/WorldStreaming.md: Player identity), a GUID made the first time it is asked for and kept in the file. Unlike
/// the game save they are written on every machine, clients included, and never replicated. The store is the
/// <see cref="ISaveStore"/> registered under the <see cref="FileName"/> key. A file written by a newer build is never
/// overwritten: its settings are not used, and changes this session are kept in memory only.
/// </summary>
public sealed class UserSettings
{
    public const string FileName = "settings.json";

    private readonly ISaveStore store;
    private string inputBindingOverrides = string.Empty;
    private string playerId = string.Empty;
    private bool isReadOnly;

    /// <summary>
    /// Creates the settings over the keyed settings <paramref name="store"/> and loads them right away.
    /// </summary>
    public UserSettings([Key(FileName)] ISaveStore store)
    {
        this.store = store;
        Load();
    }

    /// <summary>The key binding overrides as written by the Input System, or an empty string for the defaults.</summary>
    public string InputBindingOverrides => inputBindingOverrides;

    /// <summary>
    /// This install's player id, which a joining client sends the host. The first read makes a new GUID and writes the
    /// file; when the file is read-only (from a newer build) the id lasts for this session only.
    /// </summary>
    public string PlayerId
    {
        get
        {
            if (string.IsNullOrEmpty(playerId))
            {
                playerId = Guid.NewGuid().ToString("N");
                Write();
            }

            return playerId;
        }
    }

    /// <summary>Whether the file on disk came from a newer build, so changes are not written to it.</summary>
    public bool IsReadOnly => isReadOnly;

    /// <summary>
    /// Keeps <paramref name="overrides"/> and writes the file, unless it came from a newer build. Returns whether it was written.
    /// </summary>
    public bool SetInputBindingOverrides(string overrides)
    {
        inputBindingOverrides = overrides ?? string.Empty;
        return Write();
    }

    /// <summary>
    /// Writes every setting to the file, unless it came from a newer build. Returns whether it was written.
    /// </summary>
    private bool Write()
    {
        return !isReadOnly && store.Write(new UserSettingsData(inputBindingOverrides, playerId).ToJson());
    }

    /// <summary>
    /// Reads the stored settings, keeping the defaults (with a warning) when the file cannot be parsed or has another
    /// format version; a file from a newer build also makes these settings read-only.
    /// </summary>
    private void Load()
    {
        if (!store.TryRead(out string json))
        {
            return;
        }

        if (!UserSettingsData.TryParse(json, out UserSettingsData data))
        {
            Debug.LogWarning("The settings file could not be read; using the default settings.");
            return;
        }

        if (data.Version > UserSettingsData.CurrentVersion)
        {
            isReadOnly = true;
            Debug.LogWarning($"The settings file was written by a newer build (format version {data.Version}, this build reads {UserSettingsData.CurrentVersion}); using the default settings and leaving the file as it is.");
            return;
        }

        if (data.Version != UserSettingsData.CurrentVersion)
        {
            Debug.LogWarning($"The settings file has format version {data.Version}, but this build reads version {UserSettingsData.CurrentVersion}; using the default settings.");
            return;
        }

        inputBindingOverrides = data.InputBindingOverrides;
        playerId = data.PlayerId;
    }
}
