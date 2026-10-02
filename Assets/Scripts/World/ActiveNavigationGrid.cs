/// <summary>
/// Gameplay-scope holder for the current area's <see cref="NavigationGrid2D"/>, so objects that outlive an area
/// (players) can path on it. Set by <see cref="AreaEntry"/> and cleared when that area's scope is disposed.
/// </summary>
public sealed class ActiveNavigationGrid
{
    public NavigationGrid2D Current { get; private set; }

    public void Set(NavigationGrid2D navigationGrid)
    {
        Current = navigationGrid;
    }

    /// <summary>
    /// Clears the holder if it still points at <paramref name="navigationGrid"/>, leaving a newer area's grid alone.
    /// </summary>
    public void Clear(NavigationGrid2D navigationGrid)
    {
        if (ReferenceEquals(Current, navigationGrid))
        {
            Current = null;
        }
    }
}
