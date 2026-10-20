using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PersistentIds"/>: mob spawn points get a <see cref="PersistentId"/> once; making a list unique
/// assigns an id to each object without one and a new id to each later holder of a shared one, keeping the first;
/// repairing a duplicate (a copy of an object, which copies its id) gives the copy a new id and leaves the original's;
/// and the groups of objects sharing an id are found. Each test builds its own objects.
/// </summary>
public class PersistentIdTests
{
    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every mob spawn point under the roots without a <see cref="PersistentId"/> gets one, a child's included, and a
    /// second pass adds none.
    /// </summary>
    [Test]
    public void SpawnPoints_GetAPersistentIdOnce()
    {
        GameObject root = Track(new GameObject("MobSpawns"));
        GameObject first = new("Weasel 1", typeof(MobSpawnPoint));
        first.transform.SetParent(root.transform, false);
        GameObject second = new("Weasel 2", typeof(MobSpawnPoint), typeof(PersistentId));
        second.transform.SetParent(root.transform, false);
        GameObject other = new("Not a spawn point");
        other.transform.SetParent(root.transform, false);

        List<PersistentId> added = PersistentIds.AddToSpawnPoints(new[] { root });

        Assert.That(added.Count, Is.EqualTo(1));
        Assert.That(added[0].gameObject, Is.SameAs(first));
        Assert.That(other.GetComponent<PersistentId>(), Is.Null);
        Assert.That(PersistentIds.AddToSpawnPoints(new[] { root }), Is.Empty);
    }

    /// <summary>
    /// Of four ids (one unset, two sharing an id, one unique), the unset one and the second holder of the shared id get
    /// new ids, the others keep theirs, and every id ends up different.
    /// </summary>
    [Test]
    public void MakeUnique_AssignsMissingIds_AndRenewsLaterDuplicates()
    {
        PersistentId unset = NewId(null);
        PersistentId original = NewId("a");
        PersistentId copy = NewId("a");
        PersistentId own = NewId("b");

        List<PersistentId> changed = PersistentIds.MakeUnique(new[] { unset, original, copy, own });

        Assert.That(changed, Is.EquivalentTo(new[] { unset, copy }));
        Assert.That(original.Id, Is.EqualTo("a"));
        Assert.That(own.Id, Is.EqualTo("b"));
        Assert.That(unset.HasId, Is.True);
        Assert.That(new HashSet<string> { unset.Id, original.Id, copy.Id, own.Id }.Count, Is.EqualTo(4));
        Assert.That(PersistentIds.MakeUnique(new[] { unset, original, copy, own }), Is.Empty, "A unique list is left alone.");
    }

    /// <summary>
    /// A duplicated object copies its original's id; repairing the copy among every loaded id gives it a new one and
    /// leaves the original's, and repairing an object whose id nobody shares changes nothing.
    /// </summary>
    [Test]
    public void RepairNew_GivesADuplicateANewId_AndKeepsTheOriginals()
    {
        PersistentId original = NewId(PersistentIds.NewId());
        string kept = original.Id;
        PersistentId duplicate = Track(Object.Instantiate(original.gameObject)).GetComponent<PersistentId>();
        Assert.That(duplicate.Id, Is.EqualTo(kept), "A copy copies the id.");

        List<PersistentId> changed = PersistentIds.RepairNew(new[] { duplicate }, new[] { original, duplicate });

        Assert.That(changed, Is.EquivalentTo(new[] { duplicate }));
        Assert.That(original.Id, Is.EqualTo(kept));
        Assert.That(duplicate.Id, Is.Not.EqualTo(kept).And.Not.Empty);
        Assert.That(PersistentIds.RepairNew(new[] { original }, new[] { original, duplicate }), Is.Empty);
    }

    /// <summary>
    /// Duplicates are found as groups, in the order their ids first appear, unset ids left out.
    /// </summary>
    [Test]
    public void Duplicates_AreGroupedById()
    {
        PersistentId a1 = NewId("a");
        PersistentId b1 = NewId("b");
        PersistentId a2 = NewId("a");
        PersistentId unset1 = NewId(null);
        PersistentId unset2 = NewId(null);

        List<KeyValuePair<string, List<PersistentId>>> duplicates = PersistentIds.Duplicates(new[] { a1, b1, a2, unset1, unset2 });

        Assert.That(duplicates.Count, Is.EqualTo(1));
        Assert.That(duplicates[0].Key, Is.EqualTo("a"));
        Assert.That(duplicates[0].Value, Is.EqualTo(new[] { a1, a2 }));
    }

    /// <summary>
    /// A new object with a <see cref="PersistentId"/> holding <paramref name="id"/> (none when null).
    /// </summary>
    private PersistentId NewId(string id)
    {
        PersistentId persistentId = Track(new GameObject("Persistent")).AddComponent<PersistentId>();
        if (id != null)
        {
            PersistentIds.SetId(persistentId, id);
        }

        return persistentId;
    }

    /// <summary>
    /// Records <paramref name="target"/> for tear down and returns it.
    /// </summary>
    private T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }
}
