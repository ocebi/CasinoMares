using System;
using System.Collections.Generic;
using CasinoMares.CameraRig;
using CasinoMares.Core;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CasinoMares.Player
{
    /// <summary>
    /// One per connected player, state authority = that player (GDD §9.2). Holds identity, appearance and the
    /// networked copy of the balance so others can show it (leaderboard, spectator panel). Chat, announcements,
    /// emotes, paw swipes and gifts go through <see cref="Social"/>.
    /// </summary>
    public class PlayerCat : NetworkBehaviour
    {
        public static readonly List<PlayerCat> All = new();
        public static PlayerCat Local { get; private set; }

        public static event Action<PlayerCat> Joined;
        public static event Action<PlayerCat> Left;

        [SerializeField] CatFurPalette _palette;
        [SerializeField] Renderer[] _furRenderers = Array.Empty<Renderer>();
        Action<Material> _onFurLoaded;
        [SerializeField] Transform _cameraTarget;
        [SerializeField] Transform _nameplateAnchor;

        [Networked, OnChangedRender(nameof(OnAppearanceChanged))]
        public NetworkString<_16> DisplayName { get; set; }

        [Networked, OnChangedRender(nameof(OnAppearanceChanged))]
        public byte FurIndex { get; set; }

        [Networked]
        public int Balance { get; set; }

        /// <summary>
        /// Net chips earned since joining (GDD §7.4): every game's net result plus paw, cat-wall and tipsy-bonus
        /// rewards, not the welfare refill. The owner adds them up (<see cref="SessionWinnings"/>); the leaderboard
        /// ranks by it.
        /// </summary>
        [Networked]
        public int SessionNet { get; set; }

        /// <summary>Set by the owner while sitting at a game table; drives the sit animation everywhere.</summary>
        [Networked]
        public NetworkBool IsSeated { get; set; }

        /// <summary>Set by the owner while its browser tab is in the background, where it only runs about once a second.</summary>
        [Networked]
        public NetworkBool IsAway { get; set; }

        /// <summary>
        /// The owner's round trip to the Photon cloud in milliseconds, kept roughly current by the owner. The table
        /// authority holds events back by the slowest one so they show on every screen at once (<see cref="TableReveal"/>).
        /// </summary>
        [Networked]
        public short RttMs { get; set; }

        /// <summary>
        /// Paddling instead of walking (plan §11.1, §11.2): the Swim animation, a sunk and nose-up body, slower and
        /// no sprint, jump, emotes or paw. Owner writes, everyone reads. CatMovement sets it when the cat steps into
        /// the terrace pool and clears it when it steps out; <c>?swim=1</c> on the web or F9 in the editor turns it on
        /// anywhere.
        /// </summary>
        [Networked]
        public NetworkBool Swimming { get; set; }

        /// <summary>Raised on every client when the name or fur color changes.</summary>
        public event Action AppearanceChanged;

        public Transform NameplateAnchor => _nameplateAnchor != null ? _nameplateAnchor : transform;
        public Color Swatch => _palette.Get(FurIndex).Swatch;
        public bool IsLocal => Object != null && Object.HasStateAuthority;
        public PlayerRef Owner => Object != null ? Object.StateAuthority : PlayerRef.None;
        public CatReactions Reactions => _reactions;
        public CatAnimatorDriver AnimatorDriver => _animatorDriver;
        public SessionWinnings Winnings { get; private set; }
        /// <summary>The wine this cat has drunk (GDD §4.3).</summary>
        public CatDrinking Drinking { get; private set; }
        public CatSocial Social { get; private set; }
        /// <summary>The victory cigar and its networked state, on the same object.</summary>
        public CatCigar Cigar { get; private set; }

        const float RttUpdateInterval = 1f;
        const int RttChangeMs = 10;

        CatReactions _reactions;
        CatAnimatorDriver _animatorDriver;
        float _nextRttUpdate;

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
            Social = GetComponent<CatSocial>();
            Cigar = GetComponent<CatCigar>();
            Drinking = GetComponent<CatDrinking>();
        }

        public override void Render()
        {
            if (_animatorDriver != null)
            {
                _animatorDriver.Sitting = IsSeated;
                _animatorDriver.Swimming = Swimming;
            }

            if (HasStateAuthority)
            {
                UpdateRtt();
                if (Application.isEditor && GameInput.KeyPressed(Key.F9))
                    Swimming = !Swimming;
            }
        }

        /// <summary>About once a second, and only when it moved noticeably, so it doesn't churn the network.</summary>
        void UpdateRtt()
        {
            if (Time.unscaledTime < _nextRttUpdate)
                return;
            _nextRttUpdate = Time.unscaledTime + RttUpdateInterval;
            var rtt = Mathf.Clamp((int)Math.Round(Runner.GetPlayerRtt(Runner.LocalPlayer) * 1000.0), 0, short.MaxValue);
            if (Mathf.Abs(rtt - RttMs) >= RttChangeMs)
                RttMs = (short)rtt;
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
                Swimming = Platform.QueryParameter("swim") == "1";
                // After the name is set: the light's chat line names the cat.
                if (Platform.QueryParameter("cigar") == "1" && Cigar != null)
                    Cigar.LightCigar();
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
            ApplyFur();

            gameObject.name = $"PlayerCat [{DisplayName}]";
            AppearanceChanged?.Invoke();
        }

        /// <summary>The fur shows the black fallback until its own has loaded (<see cref="CatFurPalette.Resolve"/>).</summary>
        void ApplyFur()
        {
            _onFurLoaded ??= _ => { if (this != null) ApplyFur(); };
            var material = _palette.Resolve(FurIndex, _onFurLoaded);
            foreach (var furRenderer in _furRenderers)
                SetFur(furRenderer, material);
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
