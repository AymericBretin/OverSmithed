using System;
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
using Object = UnityEngine.Object;

public class TrashBinTests
{
    private NetworkManager _host;
    private NetworkManager _client;
    private GameObject _playerPrefab;
    private NetworkItem _itemPrefab;
    private GameObject _trashBinPrefab;
    private ItemDefinition _itemDefinition;
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
        _trashBinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Props/TrashBin.prefab");
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(
            AssetDatabase.FindAssets("t:ItemDatabase")[0]));
        _itemDefinition = database.Items[0];

        _host = CreateManager("Trash bin test host");
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
    public IEnumerator DiscardsHeldItemForHostAndClient()
    {
        _client = CreateManager("Trash bin test client");
        Assert.That(_client.StartClient(), Is.True);
        yield return WaitFor(() => _client.IsConnectedClient, 5f);
        Assert.That(_client.IsConnectedClient, Is.True);

        var player = Object.Instantiate(_playerPrefab, Vector3.zero, Quaternion.identity)
            .GetComponent<NetworkPlayer>();
        player.Setup(new RosterEntry { PlayerNumber = 1, LocalIndex = 1 }, true);
        player.NetworkObject.Spawn();
        var hands = player.GetComponent<PlayerHands>();

        var trashBin = Object.Instantiate(_trashBinPrefab, new Vector3(0f, 0f, 1.5f), Quaternion.identity)
            .GetComponent<TrashBin>();
        trashBin.NetworkObject.Spawn();
        yield return WaitFor(() => _client.SpawnManager.SpawnedObjects.ContainsKey(trashBin.NetworkObjectId));

        Assert.That(hands.TryInteract(trashBin.NetworkObject), Is.False, "Empty hands must do nothing");

        var item = NetworkItem.Create(_itemPrefab, _itemDefinition, new Vector3(0f, 0.7f, 0.8f),
            Quaternion.identity);
        Assert.That(item, Is.Not.Null);
        var itemId = item.NetworkObjectId;
        Assert.That(hands.TryInteract(item.NetworkObject), Is.True);
        yield return WaitFor(() => _client.SpawnManager.SpawnedObjects.ContainsKey(itemId));

        Assert.That(hands.TryInteract(trashBin.NetworkObject), Is.True);
        Assert.That(hands.IsHolding, Is.False);
        Assert.That(_host.SpawnManager.SpawnedObjects.ContainsKey(itemId), Is.False);
        yield return WaitFor(() => !_client.SpawnManager.SpawnedObjects.ContainsKey(itemId));
        Assert.That(_client.SpawnManager.SpawnedObjects.ContainsKey(itemId), Is.False,
            "The discarded item must disappear on the client too");
    }

    private NetworkManager CreateManager(string name)
    {
        var go = new GameObject(name);
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17998);
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.AddNetworkPrefab(_playerPrefab);
        manager.AddNetworkPrefab(_itemPrefab.gameObject);
        manager.AddNetworkPrefab(_trashBinPrefab);
        return manager;
    }

    private static IEnumerator WaitFor(Func<bool> condition, float seconds = 2f)
    {
        var deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
    }
}
