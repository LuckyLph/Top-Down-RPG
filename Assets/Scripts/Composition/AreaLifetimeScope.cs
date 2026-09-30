using UnityEngine;
using VContainer;
using VContainer.Unity;

public class AreaLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: area scenes must be loaded by GameFlow under a Gameplay scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        NavigationGrid2D navigationGrid = SceneQuery.FindFirst<NavigationGrid2D>(gameObject.scene);
        if (navigationGrid != null)
        {
            builder.RegisterComponent(navigationGrid);
        }

        builder.Register<AreaEntry>(Lifetime.Singleton)
            .As<IAreaEntry>()
            .WithParameter(SceneQuery.FindAll<SpawnPoint>(gameObject.scene));

        builder.RegisterBuildCallback(InjectSceneMobs);
    }

    private void InjectSceneMobs(IObjectResolver resolver)
    {
        foreach (MobController mob in SceneQuery.FindAll<MobController>(gameObject.scene))
        {
            resolver.InjectGameObject(mob.gameObject);
        }
    }
}
