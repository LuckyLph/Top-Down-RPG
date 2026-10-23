using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;

/// <summary>
/// Tests for the game's RPCs (Docs/Multiplayer.md, Rules for new code): each names who may invoke it, an owner's
/// messages the owner and the host's messages to its clients the server alone, since Netcode lets anyone invoke an RPC
/// by default and the host relays a client's <see cref="SendTo.NotServer"/> call to every other client.
/// </summary>
public class NetworkRpcTests
{
    /// <summary>
    /// Every RPC on the game's network behaviours restricts who may invoke it, and every RPC the host sends to the
    /// clients (<see cref="SendTo.NotServer"/>) only the server may invoke.
    /// </summary>
    [Test]
    public void EveryRpc_NamesWhoMayInvokeIt_AndOnlyTheServerSendsToTheClients()
    {
        List<string> rpcs = new();
        List<string> open = new();
        foreach (System.Type type in typeof(PlayerNetworkSync).Assembly.GetTypes().Where(typeof(NetworkBehaviour).IsAssignableFrom))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                CustomAttributeData rpc = method.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType == typeof(RpcAttribute));
                if (rpc == null)
                {
                    continue;
                }

                SendTo target = (SendTo)rpc.ConstructorArguments[0].Value;
                CustomAttributeNamedArgument named = rpc.NamedArguments.FirstOrDefault(argument => argument.MemberName == nameof(RpcAttribute.InvokePermission));
                RpcInvokePermission invoker = named.MemberName != null ? (RpcInvokePermission)named.TypedValue.Value : RpcInvokePermission.Everyone;
                string name = $"{type.Name}.{method.Name}";
                rpcs.Add(name);
                bool restricted = target == SendTo.NotServer ? invoker == RpcInvokePermission.Server : invoker != RpcInvokePermission.Everyone;
                if (!restricted)
                {
                    open.Add($"{name} ({target}, invoked by {invoker})");
                }
            }
        }

        Assert.That(rpcs, Has.Member($"{nameof(MobNetworkSync)}.AttackStartedRpc").And.Member($"{nameof(PlayerNetworkSync)}.CastCompletedRpc"), "The search finds the game's RPCs.");
        Assert.That(open, Is.Empty);
    }
}
