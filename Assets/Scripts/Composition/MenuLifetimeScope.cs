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

    /// <summary>
    /// Registers the scene's options menu view and controller, the Character screen's view, preview stage and
    /// controller with the outfits its preview wears (Docs/UiRevamp.md, Phase 7), and its <see cref="MainMenuController"/>.
    /// Logs an error and registers nothing without a parent scope or the gameplay settings.
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

        builder.RegisterComponentInHierarchy<OptionsMenuView>();
        builder.RegisterEntryPoint<OptionsMenuController>().AsSelf();
        builder.Register<CharacterOutfits>(Lifetime.Singleton)
            .WithParameter("spareOutfits", settings.SpareOutfits)
            .WithParameter("spritesPerFrame", settings.OutfitSpritesPerFrame);
        builder.RegisterComponentInHierarchy<CharacterScreenView>();
        builder.Register<ICharacterPreview>(resolver => resolver.Resolve<CharacterScreenView>().Stage, Lifetime.Singleton);
        builder.RegisterEntryPoint<CharacterScreenController>().AsSelf();
        builder.RegisterComponentInHierarchy<MainMenuController>();
    }
}
