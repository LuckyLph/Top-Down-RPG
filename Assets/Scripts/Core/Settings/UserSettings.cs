using UnityEngine;
using VContainer;

/// <summary>
/// The local machine's preferences, read once when created and written on every change. Unlike the game save they
/// are written on every machine, clients included, and never replicated. The store is the <see cref="ISaveStore"/>
/// registered under the <see cref="FileName"/> key.
/// </summary>
public sealed class UserSettings
{
    public const string FileName = "settings.json";

    private readonly ISaveStore store;
    private string inputBindingOverrides = string.Empty;

    public UserSettings([Key(FileName)] ISaveStore store)
    {
        this.store = store;
        Load();
    }

    /// <summary>The key binding overrides as written by the Input System, or an empty string for the defaults.</summary>
    public string InputBindingOverrides => inputBindingOverrides;

    public bool SetInputBindingOverrides(string overrides)
    {
        inputBindingOverrides = overrides ?? string.Empty;
        return store.Write(new UserSettingsData(inputBindingOverrides).ToJson());
    }

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

        if (data.Version != UserSettingsData.CurrentVersion)
        {
            Debug.LogWarning($"The settings file has format version {data.Version}, but this build reads version {UserSettingsData.CurrentVersion}; using the default settings.");
            return;
        }

        inputBindingOverrides = data.InputBindingOverrides;
    }
}
