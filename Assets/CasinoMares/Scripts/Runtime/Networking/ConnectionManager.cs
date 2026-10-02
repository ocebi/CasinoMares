using System;
using System.Collections.Generic;
using CasinoMares.Core;
using CasinoMares.Player;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasinoMares.Networking
{
    public enum ConnectionState
    {
        Offline,
        Connecting,
        Connected,
        Failed,
    }

    /// <summary>
    /// Starts and stops the Fusion Shared Mode session (GDD §9.1): one fixed room, pinned to one region,
    /// max 10 players. Spawns the local player's cat once we're in.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        /// <summary>
        /// Each casino scene is its own room, so players in the original lounge and the seaside casino never meet in
        /// half-loaded worlds, and each build's version of it is too (see <see cref="WorldFingerprint"/>).
        /// <c>?room=name</c> in the page URL (in the editor: the <see cref="TestRoomKey"/> editor pref) makes a
        /// separate room for testing.
        /// </summary>
        public static string SessionName
        {
            get
            {
                var scene = SceneManager.GetActiveScene().name;
                var name = (SceneSelect.IsClassic(scene) ? "CasinoMares-Main" : $"CasinoMares-{scene}") + "-" + WorldFingerprint();
                var room = Platform.QueryParameter("room");
#if UNITY_EDITOR
                if (string.IsNullOrEmpty(room))
                    room = UnityEditor.EditorPrefs.GetString(TestRoomKey, "");
#endif
                return string.IsNullOrEmpty(room) ? name : $"{name}-{room}";
            }
        }

        /// <summary>
        /// A short code for this build's networked world: where the scene sits in the build, and every networked
        /// object in it (and the player's cat), behaviour by behaviour. Builds that differ there can't share a room:
        /// a newcomer loads whichever scene the room's build has at that index, and Fusion stops on objects that
        /// don't match. With the code in the room name, a tab left open on an old build keeps a room of its own.
        /// </summary>
        static string WorldFingerprint()
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            static uint Hash(uint hash, string text)
            {
                foreach (var c in text)
                    hash = (hash ^ c) * prime;
                return (hash ^ '|') * prime;
            }

            static uint Behaviours(NetworkObject networkObject)
            {
                var hash = offset;
                foreach (var behaviour in networkObject.NetworkedBehaviours)
                    hash = Hash(hash, behaviour != null ? behaviour.GetType().FullName : "-");
                return hash;
            }

            var scene = SceneManager.GetActiveScene();
            var world = Hash(Hash(offset, scene.buildIndex.ToString()), SceneManager.sceneCountInBuildSettings.ToString());
            // Summed, as the objects come back in no particular order.
            uint objects = 0;
            foreach (var networkObject in FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (networkObject.gameObject.scene == scene)
                    objects += Behaviours(networkObject);
            }

            if (Instance != null && Instance._playerPrefab != null)
                world = Hash(world, Behaviours(Instance._playerPrefab).ToString());
            return ((world ^ objects) & 0xffffff).ToString("x6");
        }

        public const string TestRoomKey = "CasinoMares.TestRoom";
        public const string Region = "eu";
        public const int MaxPlayers = 10;

        /// <summary>How often a master client in a background tab looks for someone to hand the tables to.</summary>
        const float HandoffInterval = 3f;

        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] Transform[] _spawnPoints = Array.Empty<Transform>();
        [SerializeField] float _spawnScatter = 0.6f;

        public static ConnectionManager Instance { get; private set; }

        public NetworkRunner Runner { get; private set; }
        public ConnectionState State { get; private set; } = ConnectionState.Offline;
        /// <summary>
        /// Error to show on the menu. Static because a disconnect reloads the scene, which replaces this object.
        /// </summary>
        public static string LastError { get; private set; } = "";

        public event Action<ConnectionState> StateChanged;

        float _nextHandoffCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LastError = "";
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (Platform.IsWeb && State == ConnectionState.Connected && Runner != null && Runner.IsRunning)
                UpdateBackgroundTab();
        }

        /// <summary>
        /// A browser tab in the background only runs about once a second. The master client simulates every table, so
        /// a hidden master hands the role to a player whose game is in front, and the cat shows as away meanwhile.
        /// </summary>
        void UpdateBackgroundTab()
        {
            var hidden = Platform.PageHidden;
            var local = PlayerCat.Local;
            if (local != null && local.Object != null && local.Object.IsValid && local.IsAway != hidden)
                local.IsAway = hidden;

            if (!hidden || !Runner.IsSharedModeMasterClient || Time.unscaledTime < _nextHandoffCheck)
                return;

            _nextHandoffCheck = Time.unscaledTime + HandoffInterval;
            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.IsLocal || cat.Object == null || !cat.Object.IsValid || cat.IsAway)
                    continue;
                Debug.Log($"[ConnectionManager] Tab hidden: handing the tables to {cat.DisplayName}");
                Runner.SetMasterClient(cat.Owner);
                return;
            }
        }

        public async void Connect()
        {
            if (State is ConnectionState.Connecting or ConnectionState.Connected)
                return;

            LastError = "";
            SetState(ConnectionState.Connecting);

            var runnerObject = new GameObject("NetworkRunner");
            Runner = runnerObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = false;
            Runner.AddCallbacks(this);
            var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            var objectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>();

            // PhotonAppSettings.asset is gitignored (it holds the App ID), so the region is pinned here in code
            // to make sure every friend's build lands in the same room.
            var appSettings = PhotonAppSettings.Global.AppSettings.GetCopy();
            appSettings.FixedRegion = Region;

            var session = SessionName;
            Debug.Log($"[ConnectionManager] Joining room {session}");
            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = session,
                PlayerCount = MaxPlayers,
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = sceneManager,
                ObjectProvider = objectProvider,
                CustomPhotonAppSettings = appSettings,
            });

            if (result.Ok)
            {
                SetState(ConnectionState.Connected);
                return;
            }

            LastError = result.ShutdownReason == ShutdownReason.GameIsFull
                ? "The casino is full (10 players)."
                : $"Could not connect: {result.ShutdownReason}";
            Debug.LogWarning($"[ConnectionManager] StartGame failed: {result.ShutdownReason} {result.ErrorMessage}");
            CleanupRunner();
            SetState(ConnectionState.Failed);
        }

        /// <summary>Leaves the session and reloads the scene so everything starts clean.</summary>
        public async void Leave()
        {
            SetState(ConnectionState.Offline);
            if (Runner != null)
                await Runner.Shutdown();

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void SetState(ConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        void CleanupRunner()
        {
            if (Runner != null)
                Destroy(Runner.gameObject);
            Runner = null;
        }

        void SpawnLocalPlayer(NetworkRunner runner)
        {
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (_spawnPoints.Length > 0)
            {
                var point = _spawnPoints[runner.LocalPlayer.PlayerId % _spawnPoints.Length];
                var scatter = UnityEngine.Random.insideUnitCircle * _spawnScatter;
                position = point.position + new Vector3(scatter.x, 0f, scatter.y);
                rotation = point.rotation;
            }

            var playerObject = runner.Spawn(_playerPrefab, position, rotation, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, playerObject);
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Shared Mode: every client spawns only its own cat.
            if (player == runner.LocalPlayer)
                SpawnLocalPlayer(runner);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (State != ConnectionState.Connected)
                return;

            // A clean shutdown (quitting, leaving play mode) isn't an error and must not reload the scene.
            if (shutdownReason == ShutdownReason.Ok)
            {
                Runner = null;
                SetState(ConnectionState.Offline);
                return;
            }

            // Unexpected shutdown (network loss etc.). Go back to the menu with a message.
            LastError = $"Disconnected: {shutdownReason}";
            Runner = null;
            SetState(ConnectionState.Failed);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.LogWarning($"[ConnectionManager] Disconnected from server: {reason}");
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
#pragma warning disable CS0618 // Obsolete in Fusion 2.1 but still part of the interface.
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
    }
}
