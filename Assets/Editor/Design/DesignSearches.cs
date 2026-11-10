using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine.Search;

/// <summary>
/// The tuning views (Docs/DesignSystem.md, Tuning): a menu item per kind of content under <see cref="MenuRoot"/> opens
/// the Search window in its table view over every asset of that kind, a column per serialized field (the asset's
/// script left out) after the asset's name, read from the assets each time it opens.
/// </summary>
public static class DesignSearches
{
    public const string MenuRoot = "Tools/TopDownRPG/Design/Tune/";
    public const string AssetProvider = "asset";
    public const string NameColumn = "Name";

    /// <summary>
    /// The query that finds every asset of <paramref name="kind"/>, a type name, and nothing that only holds one, such as
    /// a prefab with a component whose name starts with it.
    /// </summary>
    public static string QueryFor(string kind)
    {
        return $"t={kind}";
    }

    /// <summary>
    /// The table for <paramref name="kind"/>: the name column, then a column per serialized field of the assets
    /// <paramref name="context"/> finds, but the script.
    /// </summary>
    public static SearchTable Table(SearchContext context, string kind)
    {
        List<SearchColumn> columns = new();
        string fields = kind + "/";
        using (ISearchList items = SearchService.Request(context, SearchFlags.Synchronous))
        {
            foreach (SearchColumn column in SearchColumn.Enumerate(context, items))
            {
                bool isName = column.path == NameColumn && columns.Count == 0;
                bool isField = column.path.StartsWith(fields) && column.selector != "#m_Script";
                if (isName || isField)
                {
                    columns.Add(column);
                }
            }
        }

        return new SearchTable(kind, columns);
    }

    /// <summary>
    /// The Search window's state for <paramref name="kind"/>: its query, in the table view with <see cref="Table"/>.
    /// The caller shows it, or disposes its context.
    /// </summary>
    public static SearchViewState ViewState(string kind)
    {
        SearchTable table;
        using (SearchContext lookup = SearchService.CreateContext(AssetProvider, QueryFor(kind)))
        {
            table = Table(lookup, kind);
        }

        SearchContext context = SearchService.CreateContext(AssetProvider, QueryFor(kind));
        return new SearchViewState(context, table, SearchViewFlags.TableView) { title = kind };
    }

    /// <summary>
    /// Opens the Search window's table view over every weapon.
    /// </summary>
    [MenuItem(MenuRoot + "Weapons")]
    private static void Weapons()
    {
        SearchService.ShowWindow(ViewState(nameof(PlayerWeapon)));
    }

    /// <summary>
    /// Opens the Search window's table view over every ability.
    /// </summary>
    [MenuItem(MenuRoot + "Abilities")]
    private static void Abilities()
    {
        SearchService.ShowWindow(ViewState(nameof(AbilityDefinition)));
    }

    /// <summary>
    /// Opens the Search window's table view over every status.
    /// </summary>
    [MenuItem(MenuRoot + "Statuses")]
    private static void Statuses()
    {
        SearchService.ShowWindow(ViewState(nameof(StatusEffectDefinition)));
    }

    /// <summary>
    /// Opens the Search window's table view over every mob config.
    /// </summary>
    [MenuItem(MenuRoot + "Mob Configs")]
    private static void MobConfigs()
    {
        SearchService.ShowWindow(ViewState(nameof(MobConfig)));
    }
}
