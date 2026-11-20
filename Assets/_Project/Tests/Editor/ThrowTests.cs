using System.Collections;
using NUnit.Framework;
using OverSmithed.Counters;
using OverSmithed.Items;
using OverSmithed.Networking;
using OverSmithed.Players;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class ThrowTests
{
    private NetworkManager _host;

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            if (_host != null)
            {
                _host.Shutdown();
                Object.Destroy(_host.gameObject);
            }
            yield return new ExitPlayMode();
        }
    }

    [UnityTest]
    public IEnumerator ThrowCatchAndCounterRegression()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.playModeStartScene = null;
        yield return new EnterPlayMode();
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NetworkPlayer.prefab");
        var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NetworkItem.prefab");
        var counterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Graybox/Counter_Graybox.prefab");
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(
            AssetDatabase.FindAssets("t:ItemDatabase")[0]));
        var go = new GameObject("Throw test host");
        _host = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17997);
        _host.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        _host.AddNetworkPrefab(playerPrefab);
        _host.AddNetworkPrefab(itemPrefab);
        Assert.That(_host.StartHost(), Is.True);
        yield return null;

        var sender = SpawnPlayer(playerPrefab, Vector3.zero, Quaternion.identity, 1);
        var receiver = SpawnPlayer(playerPrefab, new Vector3(0, 0, 4), Quaternion.Euler(0, 180, 0), 2);
        // No floor between the players: a successful exchange necessarily flies over a gap.
        foreach (var definition in database.Items)
        {
            var item = NetworkItem.Create(itemPrefab.GetComponent<NetworkItem>(), definition,
                new Vector3(0, 0.7f, 0.8f), Quaternion.identity);
            Assert.That(sender.TryInteract(item.NetworkObject), Is.True);
            Assert.That(sender.TryThrow(), Is.True);
            yield return new WaitForFixedUpdate();
            Assert.That(sender.IsHolding, Is.False, "The thrower must not catch its own outgoing item");
            for (var i = 0; i < 40 && !receiver.IsHolding; i++) yield return new WaitForFixedUpdate();
            Assert.That(receiver.HeldItem, Is.EqualTo(item), definition.name + " must be caught across the gap");
            Assert.That(receiver.TryThrow(), Is.True);
            for (var i = 0; i < 40 && !sender.IsHolding; i++) yield return new WaitForFixedUpdate();
            Assert.That(sender.HeldItem, Is.EqualTo(item), "Return throw: " + definition.name);
            item.NetworkObject.Despawn();
        }
        Debug.Log("THROW TEST: every item exchanged in both directions across a gap");

        var occupied = NetworkItem.Create(itemPrefab.GetComponent<NetworkItem>(), database.Items[0],
            receiver.transform.position + receiver.transform.forward * 0.8f, Quaternion.identity);
        Assert.That(receiver.TryInteract(occupied.NetworkObject), Is.True);
        var rejected = NetworkItem.Create(itemPrefab.GetComponent<NetworkItem>(), database.Items[0],
            new Vector3(0, 0.7f, 0.8f), Quaternion.identity);
        Assert.That(sender.TryInteract(rejected.NetworkObject), Is.True);
        Assert.That(sender.TryThrow(), Is.True);
        for (var i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
        Assert.That(receiver.HeldItem, Is.EqualTo(occupied));
        Assert.That(rejected.transform.parent, Is.Null);
        occupied.NetworkObject.Despawn();
        if (rejected.IsSpawned) rejected.NetworkObject.Despawn();
        Debug.Log("THROW TEST: occupied hands reject a flying item");

        receiver.transform.rotation = Quaternion.identity;
        var behind = NetworkItem.Create(itemPrefab.GetComponent<NetworkItem>(), database.Items[0],
            new Vector3(0, 0.7f, 0.8f), Quaternion.identity);
        Assert.That(sender.TryInteract(behind.NetworkObject), Is.True);
        Assert.That(sender.TryThrow(), Is.True);
        for (var i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
        Assert.That(receiver.IsHolding, Is.False, "A player facing away cannot catch");
        if (behind.IsSpawned) behind.NetworkObject.Despawn();
        Debug.Log("THROW TEST: facing away rejects a flying item");

        var counter = Object.Instantiate(counterPrefab, new Vector3(5, 0, 0), Quaternion.identity).GetComponent<Counter>();
        counter.NetworkObject.Spawn();
        var landing = NetworkItem.Create(itemPrefab.GetComponent<NetworkItem>(), database.Items[0],
            new Vector3(5, 2, 0), Quaternion.identity);
        landing.StartPhysics(Vector3.down, true);
        for (var i = 0; i < 60 && counter.IsEmpty; i++) yield return new WaitForFixedUpdate();
        Assert.That(counter.Item, Is.EqualTo(landing));
        Assert.That(landing.HasPhysics, Is.False);
        yield return new WaitForSeconds(0.4f);
        Assert.That(Vector3.Distance(landing.transform.position, counter.ItemPoint.position), Is.LessThan(0.01f));
        Debug.Log("THROW TEST: thrown item stays on an empty counter");
        _host.Shutdown();
        yield return null;
        Object.Destroy(go);
        yield return new ExitPlayMode();
    }

    private static PlayerHands SpawnPlayer(GameObject prefab, Vector3 position, Quaternion rotation, byte number)
    {
        var player = Object.Instantiate(prefab, position, rotation).GetComponent<NetworkPlayer>();
        player.Setup(new RosterEntry { PlayerNumber = number, LocalIndex = number }, true);
        player.NetworkObject.Spawn();
        return player.GetComponent<PlayerHands>();
    }
}
