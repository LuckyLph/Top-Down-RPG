using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of the main menu scene, a child of the Main scope created as <see cref="GameFlow"/> loads
/// the menu.
/// </summary>
public class MenuLifetimeScope : LifetimeScope
{
    [SerializeField, Tooltip("The gameplay settings, whose outfit numbers the Character screen's preview composes outfits with.")]
    private GameplaySettings settings;

    [SerializeField, Tooltip("The HUD settings, whose message time Play together's Copy buttons say \"Copied\" for.")]
    private HudSettings hudSettings;

    /// <summary>
    /// Registers the scene's options menu view and controller, the Character screen's view, preview stage and
    /// controller with the outfits its preview wears (Docs/UiRevamp.md, Phase 7), the session starter, the Continue line,
    /// Play together's view and controller with the clipboard its Copy buttons use (Phase 9), and its
    /// <see cref="MainMenuController"/>. Logs an error and registers nothing without a parent scope, the gameplay
    /// settings or the HUD settings.
    /// </summary>
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: the main menu must be loaded by GameFlow from the Main scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        if (settings == null)
        {
            Debug.LogError($"{name} has no {nameof(GameplaySettings)} assigned; the Character screen cannot compose outfits.", this);
            return;
        }

        if (hudSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(HudSettings)} assigned; Play together cannot time its Copy buttons.", this);
            return;
        }

        builder.RegisterInstance(hudSettings);
        builder.RegisterComponentInHierarchy<OptionsMenuView>();
        builder.RegisterEntryPoint<OptionsMenuController>().AsSelf();
        builder.Register<CharacterOutfits>(Lifetime.Singleton)
            .WithParameter("spareOutfits", settings.SpareOutfits)
            .WithParameter("spritesPerFrame", settings.OutfitSpritesPerFrame);
        builder.RegisterComponentInHierarchy<CharacterScreenView>();
        builder.Register<ICharacterPreview>(resolver => resolver.Resolve<CharacterScreenView>().Stage, Lifetime.Singleton);
        builder.RegisterEntryPoint<CharacterScreenController>().AsSelf();
        builder.Register<MenuSessionStarter>(Lifetime.Singleton);
        builder.Register<ContinueLine>(Lifetime.Singleton);
        builder.Register<SystemClipboard>(Lifetime.Singleton).As<IClipboard>();
        builder.RegisterComponentInHierarchy<PlayTogetherView>();
        builder.RegisterEntryPoint<PlayTogetherController>().AsSelf();
        builder.RegisterComponentInHierarchy<MainMenuController>();
    }
}
