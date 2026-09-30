using UnityEngine;
using VContainer;
using VContainer.Unity;

// Scope of the MainMenu scene. GameFlow parents it to the Main scope while loading the scene.
public class MenuLifetimeScope : LifetimeScope
{
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

        builder.RegisterComponentInHierarchy<MainMenuController>();
    }
}
