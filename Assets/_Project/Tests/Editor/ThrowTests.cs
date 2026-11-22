using System;
using System.Collections;
using System.Reflection;
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
using Object = UnityEngine.Object;

public class ThrowTests
{
    private NetworkManager _host;
    private NetworkManager _client;
    private GameObject _playerPrefab;
    private NetworkItem _itemPrefab;
    private GameObject _counterPrefab;
    private ItemDatabase _database;
    private SceneAsset _playModeStartScene;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        _playModeStartScene = EditorSceneManager.playModeStartScene;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.playModeStartScene = null;
        yield return new EnterPlayMode();
        _playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NetworkPlayer.prefab");
        _itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NetworkItem.prefab")
            .GetComponent<NetworkItem>();
        _counterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Graybox/Counter_Graybox.prefab");
        _database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(
            AssetDatabase.FindAssets("t:ItemDatabase")[0]));
        _host = CreateManager("Throw test host");
        Assert.That(_host.StartHost(), Is.True);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            if (_client != null) _client.Shutdown();
            if (_host != null) _host.Shutdown();
            yield return null;
            if (_client != null) Object.Destroy(_client.gameObject);
            if (_host != null) Object.Destroy(_host.gameObject);
            yield return null;
            yield return new ExitPlayMode();
        }
        EditorSceneManager.playModeStartScene = _playModeStartScene;
    }

    [UnityTest]
    public IEnumerator ThrowCatchAndCounterRegression()
    {
        var sender = SpawnPlayer(Vector3.zero, Quaternion.identity, 1);
        var receiver = SpawnPlayer(new Vector3(0, 0, 4), Quaternion.Euler(0, 180, 0), 2);
        // No floor between the players: every item must cross the gap in both directions.
        foreach (var definition in _database.Items)
        {
            var item = CreateItem(new Vector3(0, 0.7f, 0.8f), definition);
            Assert.That(sender.TryInteract(item.NetworkObject), Is.True);
            Assert.That(sender.TryThrow(), Is.True);
            Assert.That(sender.IsHolding, Is.False);
            yield return WaitFor(() => receiver.IsHolding);
            Assert.That(receiver.HeldItem, Is.EqualTo(item), definition.name + " must be caught across the gap");
            Assert.That(receiver.TryThrow(), Is.True);
            yield return WaitFor(() => sender.IsHolding);
            Assert.That(sender.HeldItem, Is.EqualTo(item), "Return throw: " + definition.name);
            item.NetworkObject.Despawn();
            Assert.That(sender.IsHolding, Is.False);
        }

        var occupied = CreateItem(receiver.transform.position + receiver.transform.forward * 0.8f);
        Assert.That(receiver.TryInteract(occupied.NetworkObject), Is.True);
        var rejected = CreateItem(new Vector3(0, 0.7f, 0.8f));
        Assert.That(sender.TryInteract(rejected.NetworkObject), Is.True);
        Assert.That(sender.TryThrow(), Is.True);
        yield return Delay(0.7f);
        Assert.That(receiver.HeldItem, Is.EqualTo(occupied), "Full hands must keep their original item");
        Assert.That(rejected == null || rejected.transform.parent == null, Is.True);
        occupied.NetworkObject.Despawn();
        if (rejected != null && rejected.IsSpawned) rejected.NetworkObject.Despawn();

        receiver.transform.rotation = Quaternion.identity;
        var behind = CreateItem(new Vector3(0, 0.7f, 0.8f));
        Assert.That(sender.TryInteract(behind.NetworkObject), Is.True);
        Assert.That(sender.TryThrow(), Is.True);
        yield return Delay(0.7f);
        Assert.That(receiver.IsHolding, Is.False, "A player facing away cannot catch");
        if (behind != null && behind.IsSpawned) behind.NetworkObject.Despawn();

        var counter = SpawnCounter(new Vector3(5, 0, 0));
        var landing = CreateItem(new Vector3(5, 2, 0));
        landing.StartPhysics(Vector3.down, thrown: true);
        yield return WaitFor(() => !counter.IsEmpty);
        Assert.That(counter.Item, Is.EqualTo(landing));
        Assert.That(landing.HasPhysics, Is.False);
        Assert.That(landing.IsThrown, Is.False);
        yield return Delay(0.4f);
        Assert.That(Vector3.Distance(landing.transform.position, counter.ItemPoint.position), Is.LessThan(0.01f));
    }

    [UnityTest]
    public IEnumerator FastThrowIsCaughtButWallsBlockIt()
    {
        var receiver = SpawnPlayer(new Vector3(0, 0, 4), Quaternion.Euler(0, 180, 0), 1);
        var fast = CreateItem(new Vector3(0, 0.7f, 1));
        fast.StartPhysics(Vector3.forward * 250f, thrown: true);
        yield return WaitFor(() => receiver.IsHolding);
        Assert.That(receiver.HeldItem, Is.EqualTo(fast), "A throw crossing a player in one physics step must be caught");
        fast.NetworkObject.Despawn();

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1, 2.5f);
        wall.transform.localScale = new Vector3(3, 3, 0.3f);
        Physics.SyncTransforms();
        var blocked = CreateItem(new Vector3(0, 0.7f, 1));
        blocked.StartPhysics(Vector3.forward * 250f, thrown: true);
        yield return Delay(0.3f);
        Assert.That(receiver.IsHolding, Is.False, "The item must hit the wall before the receiver");
        Assert.That(blocked == null || !blocked.IsThrown, Is.True);
    }

    [UnityTest]
    public IEnumerator DroppedAndLandedItemsAreNotAutomaticallyCaught()
    {
        var receiver = SpawnPlayer(Vector3.zero, Quaternion.identity, 1);
        var dropped = CreateItem(new Vector3(0, 0.7f, 0.6f));
        dropped.StartPhysics(Vector3.zero);
        yield return Delay(0.1f);
        Assert.That(receiver.IsHolding, Is.False, "Walking into a dropped item must still require Interact");
        Assert.That(receiver.TryCatch(dropped), Is.False);
        dropped.NetworkObject.Despawn();

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0, -0.5f, 2);
        floor.transform.localScale = new Vector3(5, 1, 5);
        Physics.SyncTransforms();
        var landed = CreateItem(new Vector3(0, 1, 2));
        landed.StartPhysics(Vector3.down, thrown: true);
        yield return WaitFor(() => !landed.IsThrown);
        Assert.That(landed.IsThrown, Is.False, "Landing on the floor ends automatic catching");
        Assert.That(receiver.TryCatch(landed), Is.False);
        Assert.That(receiver.TryInteract(landed.NetworkObject), Is.True, "The landed item remains pickable");
    }

    [UnityTest]
    public IEnumerator OccupiedCountersAndCounterSidesDoNotAcceptThrows()
    {
        var counter = SpawnCounter(Vector3.zero);
        var original = CreateItem(Vector3.zero);
        Assert.That(counter.TryPlace(original), Is.True);
        var rejected = CreateItem(new Vector3(0, 2, 0));
        rejected.StartPhysics(Vector3.down, thrown: true);
        yield return Delay(0.7f);
        Assert.That(counter.Item, Is.EqualTo(original), "A throw must not replace an existing item");
        Assert.That(rejected == null || rejected.HasPhysics, Is.True);
        if (rejected != null && rejected.IsSpawned) rejected.NetworkObject.Despawn();
        counter.Take().NetworkObject.Despawn();

        var side = CreateItem(new Vector3(0, 0.1f, -1.2f));
        side.StartPhysics(Vector3.forward * 9f, thrown: true);
        yield return Delay(0.3f);
        Assert.That(counter.IsEmpty, Is.True, "Hitting the side must not snap an item onto the counter");
    }

    [UnityTest]
    public IEnumerator OnlineExchangeAndCounterReplicate()
    {
        _client = CreateManager("Throw test client");
        _client.PrefabHandler.AddHandler(_playerPrefab, new ClientPrefabHandler(_playerPrefab));
        _client.PrefabHandler.AddHandler(_itemPrefab.gameObject, new ClientPrefabHandler(_itemPrefab.gameObject));
        _client.PrefabHandler.AddHandler(_counterPrefab, new ClientPrefabHandler(_counterPrefab));
        Assert.That(_client.StartClient(), Is.True);
        yield return WaitFor(() => _client.IsConnectedClient, 5f);
        Assert.That(_client.IsConnectedClient, Is.True);
        var sender = SpawnPlayer(Vector3.zero, Quaternion.identity, 1);
        var receiver = SpawnPlayer(new Vector3(0, 0, 4), Quaternion.Euler(0, 180, 0), 2, _client.LocalClientId);
        yield return WaitFor(() => _client.SpawnManager.SpawnedObjects.Count >= 2);
        var clientReceiver = _client.SpawnManager.SpawnedObjects[receiver.NetworkObjectId].GetComponent<PlayerHands>();
        var item = CreateItem(new Vector3(0, 0.7f, 0.8f));
        Assert.That(sender.TryInteract(item.NetworkObject), Is.True);
        Assert.That(sender.TryThrow(), Is.True);
        yield return WaitFor(() => receiver.IsHolding);
        Assert.That(receiver.HeldItem, Is.EqualTo(item));
        yield return WaitFor(() => clientReceiver.HeldItem != null);
        var clientItem = _client.SpawnManager.SpawnedObjects[item.NetworkObjectId].GetComponent<NetworkItem>();
        Assert.That(clientReceiver.HeldItem, Is.SameAs(clientItem), "The client must resolve its own replica");
        yield return WaitFor(() => clientItem.transform.parent == clientReceiver.transform);
        Assert.That(clientItem.transform.parent, Is.EqualTo(clientReceiver.transform));
        Assert.That(clientItem.HasPhysics, Is.False, "Only the host simulates free items");

        // Invoke the owner's actual RPC wrapper: the host must validate and execute this remote request.
        typeof(PlayerHands).GetMethod("ThrowRpc", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(clientReceiver, new object[] { Vector3.zero });
        yield return WaitFor(() => sender.IsHolding);
        Assert.That(sender.HeldItem, Is.EqualTo(item), "The client-owned player can throw it back across the gap");
        var clientSender = _client.SpawnManager.SpawnedObjects[sender.NetworkObjectId].GetComponent<PlayerHands>();
        yield return WaitFor(() => clientSender.HeldItem == clientItem && clientReceiver.HeldItem == null);
        Assert.That(clientSender.HeldItem, Is.SameAs(clientItem));
        Assert.That(clientReceiver.IsHolding, Is.False);
        item.NetworkObject.Despawn();

        var counter = SpawnCounter(new Vector3(5, 0, 0));
        yield return WaitFor(() => _client.SpawnManager.SpawnedObjects.ContainsKey(counter.NetworkObjectId));
        var landing = CreateItem(new Vector3(5, 2, 0));
        landing.StartPhysics(Vector3.down, thrown: true);
        yield return WaitFor(() => !counter.IsEmpty);
        Assert.That(counter.Item, Is.EqualTo(landing));
        var clientCounter = _client.SpawnManager.SpawnedObjects[counter.NetworkObjectId].GetComponent<Counter>();
        yield return WaitFor(() => clientCounter.Item != null);
        var clientLanding = _client.SpawnManager.SpawnedObjects[landing.NetworkObjectId].GetComponent<NetworkItem>();
        Assert.That(clientCounter.Item, Is.SameAs(clientLanding));
        yield return WaitFor(() => Vector3.Distance(clientLanding.transform.position, counter.ItemPoint.position) < 0.02f);
        Assert.That(Vector3.Distance(clientLanding.transform.position, counter.ItemPoint.position), Is.LessThan(0.02f));
    }

    private NetworkManager CreateManager(string name)
    {
        var go = new GameObject(name);
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17997);
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.AddNetworkPrefab(_playerPrefab);
        manager.AddNetworkPrefab(_itemPrefab.gameObject);
        manager.AddNetworkPrefab(_counterPrefab);
        return manager;
    }

    private sealed class ClientPrefabHandler : INetworkPrefabInstanceHandler
    {
        private readonly GameObject _prefab;

        public ClientPrefabHandler(GameObject prefab) => _prefab = prefab;

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            var instance = Object.Instantiate(_prefab, position, rotation);
            // Peers share a physics world in this test only. Disable replica colliders before they can
            // collide with the host's thrown items, including items spawned while a throw is in flight.
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) collider.enabled = false;
            return instance.GetComponent<NetworkObject>();
        }

        public void Destroy(NetworkObject networkObject) => Object.Destroy(networkObject.gameObject);
    }

    private NetworkItem CreateItem(Vector3 position, ItemDefinition definition = null) =>
        NetworkItem.Create(_itemPrefab, definition != null ? definition : _database.Items[0], position, Quaternion.identity);

    private Counter SpawnCounter(Vector3 position)
    {
        var counter = Object.Instantiate(_counterPrefab, position, Quaternion.identity).GetComponent<Counter>();
        counter.NetworkObject.Spawn();
        return counter;
    }

    private static IEnumerator WaitFor(Func<bool> condition, float seconds = 2f)
    {
        var deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
    }

    private static IEnumerator Delay(float seconds)
    {
        var deadline = Time.time + seconds;
        while (Time.time < deadline) yield return null;
    }

    private PlayerHands SpawnPlayer(Vector3 position, Quaternion rotation, byte number, ulong owner = 0)
    {
        var player = Object.Instantiate(_playerPrefab, position, rotation).GetComponent<NetworkPlayer>();
        player.Setup(new RosterEntry { PlayerNumber = number, LocalIndex = number }, true);
        player.NetworkObject.SpawnWithOwnership(owner);
        return player.GetComponent<PlayerHands>();
    }
}
