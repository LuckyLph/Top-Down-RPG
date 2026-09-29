using VContainer;
using VContainer.Unity;

public class MenuLifetimeScope : LifetimeScope
{
    protected override LifetimeScope FindParent()
    {
        return Find<MainLifetimeScope>();
    }

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<MainMenuController>();
    }
}
