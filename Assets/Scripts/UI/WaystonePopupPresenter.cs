using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that shows the local player its own waystone discoveries: a floating text
/// (<see cref="GameplaySettings.WaystoneDiscoveredText"/>) over the player on the pooled popup layer, on this machine
/// only, as the waystone's stone wakes (<see cref="WaystoneMarkers"/>, Docs/KnownGaps.md, Phase 3).
/// </summary>
public sealed class WaystonePopupPresenter : IStartable, IDisposable
{
    private readonly WaystoneNotices notices;
    private readonly LocalPlayerTracker localPlayer;
    private readonly DamagePopupLayer popupLayer;
    private readonly Camera camera;
    private readonly GameplaySettings settings;

    /// <summary>
    /// Captures where discoveries are told, the local player, the popup layer and camera it draws on, and the text.
    /// </summary>
    public WaystonePopupPresenter(WaystoneNotices notices, LocalPlayerTracker localPlayer, DamagePopupLayer popupLayer, Camera camera, GameplaySettings settings)
    {
        this.notices = notices;
        this.localPlayer = localPlayer;
        this.popupLayer = popupLayer;
        this.camera = camera;
        this.settings = settings;
    }

    /// <summary>
    /// Listens for discoveries.
    /// </summary>
    public void Start()
    {
        notices.Discovered += HandleDiscovered;
    }

    /// <summary>
    /// Stops listening.
    /// </summary>
    public void Dispose()
    {
        notices.Discovered -= HandleDiscovered;
    }

    /// <summary>
    /// Shows the text over the local player when it is the one that discovered a waystone.
    /// </summary>
    private void HandleDiscovered(PlayerHandle player, string entryName)
    {
        LocalPlayer local = localPlayer.Current;
        if (local == null || player != local.Handle || popupLayer == null || local.Transform == null)
        {
            return;
        }

        Vector3 position = local.Transform.TryGetComponent(out DamageReceiver receiver) ? receiver.PopupWorldPosition : local.Transform.position;
        popupLayer.Spawn(settings.WaystoneDiscoveredText, settings.WaystoneDiscoveredColor, position, camera);
    }
}
