using System;
using System.Collections.Generic;
using CasinoMares.CameraRig;
using CasinoMares.Core;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// One per connected player, state authority = that player (GDD §9.2). Holds identity, appearance and the
    /// networked copy of the balance so others can show it (leaderboard, spectator panel).
    /// </summary>
    public class PlayerCat : NetworkBehaviour
    {
        public static readonly List<PlayerCat> All = new();
        public static PlayerCat Local { get; private set; }

        public static event Action<PlayerCat> Joined;
        public static event Action<PlayerCat> Left;

        [SerializeField] CatFurPalette _palette;
        [SerializeField] Renderer[] _furRenderers = Array.Empty<Renderer>();
        [SerializeField] Transform _cameraTarget;
        [SerializeField] Transform _nameplateAnchor;

        [Networked, OnChangedRender(nameof(OnAppearanceChanged))]
        public NetworkString<_16> DisplayName { get; set; }

        [Networked, OnChangedRender(nameof(OnAppearanceChanged))]
        public byte FurIndex { get; set; }

        [Networked]
        public int Balance { get; set; }

        /// <summary>Set by the owner while sitting at a game table; drives the sit animation everywhere.</summary>
        [Networked]
        public NetworkBool IsSeated { get; set; }

        /// <summary>Set by the owner while its browser tab is in the background, where it only runs about once a second.</summary>
        [Networked]
        public NetworkBool IsAway { get; set; }

        /// <summary>Raised on every client when the name or fur color changes.</summary>
        public event Action AppearanceChanged;

        public Transform NameplateAnchor => _nameplateAnchor != null ? _nameplateAnchor : transform;
        public Color Swatch => _palette.Get(FurIndex).Swatch;
        public bool IsLocal => Object != null && Object.HasStateAuthority;
        public PlayerRef Owner => Object != null ? Object.StateAuthority : PlayerRef.None;
        public CatReactions Reactions => _reactions;
        public SessionWinnings Winnings { get; private set; }

        CatReactions _reactions;
        CatAnimatorDriver _animatorDriver;

        /// <summary>The cat owned by this player, if it has spawned on this client.</summary>
        public static PlayerCat Find(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return null;
            foreach (var cat in All)
            {
                if (cat != null && cat.Object != null && cat.Object.IsValid && cat.Object.StateAuthority == player)
                    return cat;
            }

            return null;
        }

        public static string NameOf(PlayerRef player)
        {
            var cat = Find(player);
            return cat != null ? cat.DisplayName.ToString() : "A cat";
        }

        void Awake()
        {
            _reactions = GetComponentInChildren<CatReactions>();
            _animatorDriver = GetComponent<CatAnimatorDriver>();
            Winnings = GetComponent<SessionWinnings>();
        }

        public override void Render()
        {
            if (_animatorDriver != null)
                _animatorDriver.Sitting = IsSeated;
        }

        // ---------------------------------------------------------------- Chat & announcements

        /// <summary>Send a chat line from the local player to everyone (GDD §9.5).</summary>
        public void SendChat(string text)
        {
            text = ChatLog.Sanitize(text);
            if (text.Length > 0)
                RPC_Chat(text);
        }

        /// <summary>Broadcast a system line such as "Mochi gifted 200 chips to Luna".</summary>
        public void Announce(string text) => RPC_Announce(ChatLog.Sanitize(text));

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Chat(string text)
        {
            ChatLog.AddPlayer(DisplayName.ToString(), Swatch, text);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Announce(string text)
        {
            ChatLog.AddSystem(text);
        }

        // ---------------------------------------------------------------- Emotes and paw swipes

        /// <summary>Play an emote on this cat for everyone. Only standing cats emote.</summary>
        public void PlayEmote(CatEmote emote)
        {
            if (IsLocal && !IsSeated && emote != CatEmote.None)
                RPC_Emote((byte)emote);
        }

        /// <summary>Show a paw swipe on this cat for everyone.</summary>
        public void PlaySwipe()
        {
            if (IsLocal)
                RPC_Swipe();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Emote(byte emote) => _reactions?.PlayEmote((CatEmote)emote);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Swipe() => _reactions?.PlaySwipe();

        // ---------------------------------------------------------------- Gifting (GDD §7.3)

        /// <summary>Called by the sender after taking the chips out of their own balance.</summary>
        public void SendGift(int amount, string fromName) => RPC_ReceiveGift(amount, fromName);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ReceiveGift(int amount, string fromName)
        {
            if (amount <= 0)
                return;
            ProfileStore.SetBalance(ProfileStore.Current.Balance + amount);
            Toasts.Show($"{fromName} gifted you {amount:N0} chips!");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Local = null;
            Joined = null;
            Left = null;
        }

        public override void Spawned()
        {
            All.Add(this);

            if (HasStateAuthority)
            {
                Local = this;
                var profile = ProfileStore.Current;
                DisplayName = MakeUniqueName(profile.DisplayName);
                FurIndex = (byte)_palette.Clamp(profile.FurIndex);
                Balance = profile.Balance;
                ProfileStore.Changed += OnProfileChanged;
                PlayerCameraRig.Instance?.Follow(_cameraTarget);
            }

            ApplyAppearance();
            Joined?.Invoke(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
                ProfileStore.Changed -= OnProfileChanged;
                PlayerCameraRig.Instance?.Release();
            }

            Left?.Invoke(this);
        }

        void OnProfileChanged(PlayerProfile profile)
        {
            Balance = profile.Balance;
        }

        void OnAppearanceChanged()
        {
            ApplyAppearance();
        }

        void ApplyAppearance()
        {
            var material = _palette.Get(FurIndex).Material;
            foreach (var furRenderer in _furRenderers)
                SetFur(furRenderer, material);

            gameObject.name = $"PlayerCat [{DisplayName}]";
            AppearanceChanged?.Invoke();
        }

        /// <summary>Swap the fur material (slot 0) and keep any extra passes, such as the rim light, in place.</summary>
        public static void SetFur(Renderer furRenderer, Material material)
        {
            var materials = furRenderer.sharedMaterials;
            if (materials.Length == 0)
                return;
            materials[0] = material;
            furRenderer.sharedMaterials = materials;
        }

        /// <summary>Best effort: adds "(2)", "(3)"... if another cat already uses the name (GDD §3.2).</summary>
        string MakeUniqueName(string wanted)
        {
            wanted = ProfileStore.SanitizeName(wanted);
            var candidate = wanted;
            for (var n = 2; IsNameTaken(candidate); n++)
            {
                var suffix = $" ({n})";
                var baseLength = Mathf.Min(wanted.Length, ProfileStore.MaxNameLength - suffix.Length);
                candidate = wanted.Substring(0, baseLength) + suffix;
            }

            return candidate;
        }

        bool IsNameTaken(string name)
        {
            foreach (var cat in All)
            {
                if (cat != this && cat.Object != null && cat.Object.IsValid && cat.DisplayName.ToString() == name)
                    return true;
            }

            return false;
        }
    }
}
