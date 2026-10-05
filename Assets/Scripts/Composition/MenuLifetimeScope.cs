using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of the main menu scene, a child of the Main scope created as <see cref="GameFlow"/> loads
/// the menu.
/// </summary>
public class MenuLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers the scene's options menu view and controller and its <see cref="MainMenuController"/>. Logs an
    /// error and registers nothing without a parent scope.
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

        builder.RegisterComponentInHierarchy<OptionsMenuView>();
        builder.RegisterEntryPoint<OptionsMenuController>().AsSelf();
        builder.RegisterComponentInHierarchy<MainMenuController>();
    }
}
