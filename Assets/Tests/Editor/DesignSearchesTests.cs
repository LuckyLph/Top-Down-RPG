using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Search;

/// <summary>
/// Tests for the tuning views (Docs/DesignSystem.md, Phase 2): each kind's view opens in the table view over every
/// asset of the kind, its name first and a column per tuned field.
/// </summary>
public class DesignSearchesTests
{
    /// <summary>
    /// The view of <paramref name="kind"/> is a table whose query finds every asset of the kind, its name column first
    /// and a column for each of <paramref name="fields"/>, but none for the script.
    /// </summary>
    [TestCase(nameof(PlayerWeapon), "damage", "attackCooldown", "windupSeconds", "contactSeconds", "recoverySeconds")]
    [TestCase(nameof(AbilityDefinition), "cooldown", "castTime", "range", "hit.amount", "heal")]
    [TestCase(nameof(StatusEffectDefinition), "duration", "maxStacks", "periodicAmount", "tickInterval", "moveSpeedMultiplier")]
    [TestCase(nameof(MobConfig), "moveSpeed", "attackDamage", "attackInterval", "attackWindup", "attackContact")]
    public void EachKindsView_IsATableOfItsAssetsAndFields(string kind, params string[] fields)
    {
        SearchViewState state = DesignSearches.ViewState(kind);
        try
        {
            Assert.That(state.itemSize, Is.EqualTo((float)DisplayMode.Table));
            string[] selectors = state.tableConfig.columns.Select(column => column.selector).ToArray();
            Assert.That(state.tableConfig.columns[0].path, Is.EqualTo(DesignSearches.NameColumn));
            Assert.That(selectors, Does.Not.Contain("#m_Script"));
            foreach (string field in fields)
            {
                Assert.That(selectors, Does.Contain("#" + field), kind);
            }

            using (ISearchList items = SearchService.Request(state.context, SearchFlags.Synchronous))
            {
                Assert.That(items.Count, Is.EqualTo(AssetDatabase.FindAssets("t:" + kind).Length), $"{kind}: the view's assets");
            }
        }
        finally
        {
            state.context.Dispose();
        }
    }
}
