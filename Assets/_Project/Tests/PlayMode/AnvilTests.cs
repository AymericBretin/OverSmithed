#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using OverSmithed.Crafting;
using OverSmithed.Items;
using OverSmithed.Networking;
using OverSmithed.Players;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OverSmithed.Tests
{
    /// <summary>Real host/client regression tests using the shipping prefabs and loopback transport.</summary>
    public class AnvilTests
    {
        private const string Prefabs = "Assets/_Project/Prefabs/";
        private NetworkManager _host;
        private NetworkManager _client;
        private ushort _port;
        private Station _anvil;
        private PlayerWork _first;
        private PlayerWork _second;
        private NetworkItem _ingot;
        private static readonly MethodInfo WorkRpc = typeof(PlayerWork).GetMethod("WorkRpc", BindingFlags.Instance | BindingFlags.NonPublic);

        private static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        private static ItemDefinition Item(string name) => Asset<ItemDefinition>($"Assets/_Project/Settings/Items/Item_{name}.asset");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(NetworkManager.Singleton, Is.Null, "Tests need an isolated Play Mode scene.");
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _host = Manager("Anvil test host");
            _host.SetSingleton();
            Assert.That(_host.StartHost(), Is.True);
            _client = Manager("Anvil test client");
            Assert.That(_client.StartClient(), Is.True);
            yield return Until(() => _client.IsConnectedClient, "client connection");

            _anvil = Object.Instantiate(Asset<GameObject>(Prefabs + "Stations/Anvil.prefab")).GetComponent<Station>();
            _anvil.NetworkObject.Spawn();
            _first = Player(1, _host.LocalClientId, new Vector3(-0.8f, 0f, 0f));
            _second = Player(2, _client.LocalClientId, new Vector3(0.8f, 0f, 0f));
            _ingot = NewItem("HotIngot");
            Assert.That(_anvil.TryPlace(_ingot), Is.True);
            Physics.SyncTransforms();
            yield return Until(() => Replica<Station>(_anvil.NetworkObjectId)?.Item != null
                && Replica<PlayerWork>(_second.NetworkObjectId) != null, "replicated anvil, ingot and players");
            Assert.That(_anvil.NeedsHandWork, Is.True);
            Assert.That(_anvil.GetComponentInChildren<StationProgressBar>(), Is.Not.Null);
            Assert.That(_anvil.GetComponent<AnvilEffects>(), Is.Not.Null);
        }

        private NetworkManager Manager(string name)
        {
            var go = new GameObject(name);
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", _port, "127.0.0.1");
            var manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
                TickRate = 60,
            };
            foreach (var path in new[] { "Stations/Anvil.prefab", "NetworkItem.prefab", "NetworkPlayer.prefab" })
                manager.AddNetworkPrefab(Asset<GameObject>(Prefabs + path));
            return manager;
        }

        private PlayerWork Player(byte number, ulong owner, Vector3 position)
        {
            var go = Object.Instantiate(Asset<GameObject>(Prefabs + "NetworkPlayer.prefab"), position, Quaternion.identity);
            go.GetComponent<PlayerMovement>().enabled = false;
            go.GetComponent<PlayerFall>().enabled = false;
            go.GetComponent<NetworkPlayer>().Setup(new RosterEntry { PlayerNumber = number, LocalIndex = number, ClientId = owner }, true);
            go.GetComponent<NetworkObject>().SpawnWithOwnership(owner);
            return go.GetComponent<PlayerWork>();
        }

        private NetworkItem NewItem(string name) => NetworkItem.Create(Asset<NetworkItem>(Prefabs + "NetworkItem.prefab"), Item(name), Vector3.zero, Quaternion.identity);
        private T Replica<T>(ulong id) where T : Component => _client.SpawnManager.SpawnedObjects.TryGetValue(id, out var value) ? value.GetComponent<T>() : null;

        private void RemoteUse(bool held)
        {
            var worker = Replica<PlayerWork>(_second.NetworkObjectId);
            WorkRpc.Invoke(worker, new object[] { held
                ? new NetworkObjectReference(Replica<Station>(_anvil.NetworkObjectId).NetworkObject)
                : new NetworkObjectReference((NetworkObject)null) });
        }

        [UnityTest]
        public IEnumerator HoldPauseResumeTransformsAndReplicatesWithSparks()
        {
            var remote = Replica<Station>(_anvil.NetworkObjectId);
            yield return new WaitForSeconds(0.15f);
            Assert.That(_anvil.Progress, Is.Zero, "Placement alone must not work.");
            Assert.That(_anvil.GetComponentInChildren<ParticleSystem>().particleCount, Is.Zero);
            var hostDone = 0;
            var clientDone = 0;
            _anvil.WorkCompleted += _ => hostDone++;
            remote.WorkCompleted += _ => clientDone++;
            RemoteUse(true);
            yield return Until(() => _anvil.Progress > 0.18f && remote.IsWorking, "manual progress");
            Assert.That(_anvil.ActiveWorker, Is.SameAs(_second));
            yield return Until(() => Replica<PlayerHammerAnimation>(_second.NetworkObjectId).IsHammering
                && _second.GetComponent<PlayerHammerAnimation>().IsHammering, "remote worker animation on both machines");
            Assert.That(_first.GetComponent<PlayerHammerAnimation>().IsHammering, Is.False);
            Assert.That(remote.Progress, Is.EqualTo(_anvil.Progress).Within(0.08f));
            yield return Until(() => remote.GetComponentInChildren<ParticleSystem>().particleCount > 0, "client hammer sparks");
            RemoteUse(false);
            yield return Until(() => !_anvil.IsWorking && !remote.IsWorking, "release");
            var paused = _anvil.Progress;
            yield return new WaitForSeconds(0.5f);
            Assert.That(_anvil.Progress, Is.EqualTo(paused));
            Assert.That(remote.Progress, Is.EqualTo(paused).Within(0.001f));
            Assert.That(remote.GetComponentInChildren<ParticleSystem>().particleCount, Is.Zero, "No sparks while paused.");
            Assert.That(Replica<PlayerHammerAnimation>(_second.NetworkObjectId).IsHammering, Is.False);
            Assert.That(Replica<PlayerHammerAnimation>(_second.NetworkObjectId).Hammer.activeSelf, Is.False);
            RemoteUse(true);
            yield return Until(() => _ingot.Definition == Item("Blade") && remote.Item.Definition == Item("Blade"), "blade on host and client");
            yield return Until(() => clientDone == 1, "completion RPC");
            Assert.That(hostDone, Is.EqualTo(1));
            Assert.That(_anvil.IsWorking, Is.False);
            Assert.That(_anvil.Item, Is.SameAs(_ingot), "Conversion keeps the same network item.");
            yield return null;
            Assert.That(Replica<PlayerHammerAnimation>(_second.NetworkObjectId).IsHammering, Is.False);
        }

        [UnityTest]
        public IEnumerator OnlyOnePlayerCanClaimAnvilAndAnotherCanRetryAfterRelease()
        {
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.Progress > 0.08f, "first worker");
            RemoteUse(true);
            yield return new WaitForSeconds(0.15f);
            Assert.That(_second.CanWorkAt(_anvil), Is.False, "Second claimant must be rejected by the server.");
            Assert.That(Replica<PlayerHammerAnimation>(_second.NetworkObjectId).IsHammering, Is.False,
                "Rejected players must never display a hammer.");
            _first.HostWorkAt(null);
            yield return Until(() => !_anvil.IsWorking, "exclusive owner released");
            var paused = _anvil.Progress;
            yield return new WaitForSeconds(0.15f);
            Assert.That(_anvil.Progress, Is.EqualTo(paused), "A rejected claim cannot keep hammering.");
            RemoteUse(false);
            RemoteUse(true);
            yield return Until(() => _anvil.Progress > paused + 0.05f, "second worker retry");
        }

        [UnityTest]
        public IEnumerator LeavingReachAndDespawnReleaseTheClaim()
        {
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.Progress > 0.05f, "first worker");
            _first.transform.position = Vector3.one * 100f;
            Physics.SyncTransforms();
            yield return Until(() => !_anvil.IsWorking, "out of reach pauses");
            RemoteUse(true);
            yield return Until(() => _second.CanWorkAt(_anvil) && _anvil.IsWorking, "stale claim replaced");
            _second.NetworkObject.Despawn();
            yield return Until(() => !_anvil.IsWorking, "despawn releases claim");
            _first.HostWorkAt(null);
            _first.transform.position = new Vector3(-0.8f, 0f, 0f);
            Physics.SyncTransforms();
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.IsWorking, "claim reusable after disconnect");
        }

        [UnityTest]
        public IEnumerator WrongItemsAreRejectedAndRemovingIngotResetsProgress()
        {
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.Progress > 0.1f, "partial work");
            _first.HostWorkAt(null);
            Assert.That(_anvil.Take(), Is.SameAs(_ingot));
            Assert.That(_anvil.Progress, Is.Zero);
            Assert.That(_anvil.TryPlace(NewItem("MetalOre")), Is.False);
            Assert.That(_anvil.TryPlace(NewItem("Blade")), Is.False);
            Assert.That(_anvil.TryPlace(_ingot), Is.True);
            Assert.That(_anvil.Progress, Is.Zero);
            yield return null;
            Assert.That(_anvil.IsWorking, Is.False);
        }

        [UnityTest]
        public IEnumerator RejoiningClientReceivesPausedProgress()
        {
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.Progress > 0.15f, "partial work");
            _first.HostWorkAt(null);
            yield return Until(() => !_anvil.IsWorking, "pause");
            var paused = _anvil.Progress;
            _client.Shutdown();
            yield return Until(() => !_client.IsListening, "client shutdown");
            Object.Destroy(_client.gameObject);
            yield return null;
            _client = Manager("Rejoining anvil client");
            Assert.That(_client.StartClient(), Is.True);
            yield return Until(() => _client.IsConnectedClient && Replica<Station>(_anvil.NetworkObjectId)?.Item != null, "late join state");
            var remote = Replica<Station>(_anvil.NetworkObjectId);
            // NGO's spawn snapshot can precede a pending variable delta by one network tick.
            yield return Until(() => !remote.IsWorking, "late join pause delta");
            Assert.That(remote.IsWorking, Is.False);
            Assert.That(remote.Progress, Is.EqualTo(paused).Within(0.001f));
            Assert.That(remote.Item.Definition, Is.EqualTo(Item("HotIngot")));
        }

        [UnityTest]
        public IEnumerator AllSkinsAnimateOnClientAndHammerFollowsHand()
        {
            var library = Asset<SkinLibrary>("Assets/_Project/Settings/Skins/SkinLibrary.asset");
            Assert.That(library, Is.Not.Null);
            var player = _first.GetComponent<NetworkPlayer>();
            var remotePlayer = Replica<NetworkPlayer>(_first.NetworkObjectId);
            var animation = Replica<PlayerHammerAnimation>(_first.NetworkObjectId);
            for (byte skin = 0; skin < library.Count; skin++)
            {
                player.SetSkin(skin);
                _first.HostWorkAt(_anvil);
                yield return Until(() => remotePlayer.SkinIndex == skin && animation.IsHammering
                    && animation.Hammer != null && animation.Hammer.activeInHierarchy, "skin hammer visible " + skin);
                Assert.That(animation.Hammer.transform.parent.name, Is.EqualTo("handslot.r"));
                Assert.That(animation.Hammer.GetComponentInChildren<Renderer>().enabled, Is.True);
                var animator = remotePlayer.VisualAnimator;
                Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("Hammer")), Is.EqualTo(1f));
                var initialHand = animation.Hammer.transform.parent.rotation;
                yield return new WaitForSeconds(0.16f);
                Assert.That(Quaternion.Angle(initialHand, animation.Hammer.transform.parent.rotation), Is.GreaterThan(2f),
                    "The actual hand bones must move, not only an Animator parameter.");
                var hostPhase = player.VisualAnimator.GetFloat("HammerPhase");
                var clientPhase = animator.GetFloat("HammerPhase");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(hostPhase * 360f, clientPhase * 360f)), Is.LessThan(35f));
                _first.HostWorkAt(null);
                yield return Until(() => !animation.IsHammering, "skin stops " + skin);
                Assert.That(animation.Hammer.activeSelf, Is.False);
                Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("Hammer")), Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator RejoiningObserverSeesHostHammeringAndImpact()
        {
            _client.Shutdown();
            yield return Until(() => !_client.IsListening, "observer shutdown");
            Object.Destroy(_client.gameObject);
            yield return null;
            _first.HostWorkAt(_anvil);
            yield return Until(() => _anvil.Progress > 0.05f, "host works alone");
            _client = Manager("Late observer");
            Assert.That(_client.StartClient(), Is.True);
            yield return Until(() => _client.IsConnectedClient
                && Replica<PlayerHammerAnimation>(_first.NetworkObjectId)?.IsHammering == true, "late observer animation");
            var remote = Replica<Station>(_anvil.NetworkObjectId);
            Assert.That(remote.ActiveWorker, Is.SameAs(Replica<PlayerWork>(_first.NetworkObjectId)));
            var sparks = remote.GetComponentInChildren<ParticleSystem>();
            yield return Until(() => sparks.particleCount > 0, "late observer sees impact sparks");
            yield return new WaitForSeconds(0.1f);
            CaptureClient("online-impact");
            _first.HostWorkAt(null);
            yield return Until(() => !Replica<PlayerHammerAnimation>(_first.NetworkObjectId).IsHammering, "observer sees release");
        }

        // Render ONLY the client's replicated objects, never the host's overlapping copies in these loopback tests.
        private void CaptureClient(string name)
        {
            var hidden = _host.SpawnManager.SpawnedObjects.Values
                .SelectMany(n => n.GetComponentsInChildren<Renderer>()).Where(r => !r.forceRenderingOff).ToArray();
            foreach (var renderer in hidden) renderer.forceRenderingOff = true;
            var hostCanvases = _host.SpawnManager.SpawnedObjects.Values
                .SelectMany(n => n.GetComponentsInChildren<Canvas>()).Where(c => c.enabled).ToArray();
            foreach (var canvas in hostCanvases) canvas.enabled = false;
            var camera = new GameObject("Client verification camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(3f, 2.8f, -4f);
            camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 1.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
            var light = new GameObject("Verification key light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            var target = new RenderTexture(960, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try
            {
                foreach (var face in Replica<Station>(_anvil.NetworkObjectId).GetComponentsInChildren<OverSmithed.UI.FaceCamera>())
                    face.SendMessage("LateUpdate");
                Canvas.ForceUpdateCanvases();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                texture.Apply();
                System.IO.Directory.CreateDirectory("Temp/AnvilTests");
                System.IO.File.WriteAllBytes("Temp/AnvilTests/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.Destroy(texture);
                Object.Destroy(target);
                Object.Destroy(camera.gameObject);
                Object.Destroy(light.gameObject);
                foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
                foreach (var canvas in hostCanvases) if (canvas != null) canvas.enabled = true;
            }
        }

        private static IEnumerator Until(Func<bool> predicate, string description)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(predicate(), Is.True, "Timed out: " + description);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null) _client.Shutdown();
            if (_host != null) _host.Shutdown();
            yield return null;
            yield return null;
            if (_client != null) Object.Destroy(_client.gameObject);
            if (_host != null) Object.Destroy(_host.gameObject);
            yield return null;
        }
    }
}
#endif
