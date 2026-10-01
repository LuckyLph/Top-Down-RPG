using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using VContainer;

public class InjectingNetworkPrefabHandlerTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void Instantiate_CreatesAnInjectedCopyAtTheRequestedPose()
    {
        GameObject prefab = new("NetworkedProbe");
        createdObjects.Add(prefab);
        NetworkObject prefabNetworkObject = prefab.AddComponent<NetworkObject>();
        prefab.AddComponent<MeleeDamageDealer>();
        ManualClock clock = new() { Time = 100f };
        ContainerBuilder builder = new();
        builder.RegisterInstance<IClock>(clock);
        builder.RegisterInstance(new DamageService(new CombatEvents()));
        InjectingNetworkPrefabHandler handler = new(prefabNetworkObject, builder.Build());

        NetworkObject instance = handler.Instantiate(1, new Vector3(2f, 3f, 0f), Quaternion.Euler(0f, 0f, 90f));
        createdObjects.Add(instance.gameObject);
        MeleeDamageDealer injected = instance.GetComponent<MeleeDamageDealer>();
        injected.ResetCooldown(readyImmediately: false);

        Assert.That(instance, Is.Not.SameAs(prefabNetworkObject));
        Assert.That(injected.NextAttackTime, Is.EqualTo(100f + injected.AttackInterval), "The copy should run on the injected clock.");
        Assert.That(instance.transform.position, Is.EqualTo(new Vector3(2f, 3f, 0f)));
        Assert.That(instance.transform.rotation.eulerAngles.z, Is.EqualTo(90f).Within(0.01f));
    }
}
