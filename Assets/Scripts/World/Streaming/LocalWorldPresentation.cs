using System;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that shows only the local player's world: that world's lighting is on and every other
/// loaded world's is off, now and whenever the local player, its world or the loaded worlds change.
/// </summary>
public sealed class LocalWorldPresentation : IStartable, IDisposable
{
    private readonly Worlds worlds;
    private readonly PlayerWorlds playerWorlds;
    private readonly LocalPlayerTracker localPlayer;

    /// <summary>
    /// Captures the loaded worlds, the players' worlds and the local player.
    /// </summary>
    public LocalWorldPresentation(Worlds worlds, PlayerWorlds playerWorlds, LocalPlayerTracker localPlayer)
    {
        this.worlds = worlds;
        this.playerWorlds = playerWorlds;
        this.localPlayer = localPlayer;
    }

    /// <summary>
    /// Subscribes to the changes that move the local player's world and applies it once.
    /// </summary>
    public void Start()
    {
        worlds.WorldLoaded += HandleWorldLoaded;
        playerWorlds.Changed += HandlePlayerWorldChanged;
        localPlayer.Changed += HandleLocalPlayerChanged;
        Apply();
    }

    /// <summary>
    /// Unsubscribes.
    /// </summary>
    public void Dispose()
    {
        worlds.WorldLoaded -= HandleWorldLoaded;
        playerWorlds.Changed -= HandlePlayerWorldChanged;
        localPlayer.Changed -= HandleLocalPlayerChanged;
    }

    /// <summary>
    /// Turns on the lighting of the local player's world and off every other loaded world's.
    /// </summary>
    private void Apply()
    {
        WorldDefinition local = localPlayer.Current != null ? playerWorlds.Get(localPlayer.Current.Handle) : null;
        foreach (LoadedWorld world in worlds.Loaded)
        {
            if (world.Lighting != null)
            {
                world.Lighting.SetShown(world.Definition == local);
            }
        }
    }

    private void HandleWorldLoaded(LoadedWorld world)
    {
        Apply();
    }

    private void HandlePlayerWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        Apply();
    }

    private void HandleLocalPlayerChanged(LocalPlayer player)
    {
        Apply();
    }
}
