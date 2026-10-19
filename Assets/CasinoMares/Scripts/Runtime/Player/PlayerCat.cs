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
        /// The tick until which this cat smokes its victory cigar, 0 when it never lit one (GDD §5.8). The owner
        /// stamps it on a huge win (a later one extends it); every client, late joiners included, renders from it.
        /// </summary>
        [Networked]
        public int CigarUntilTick { get; set; }

        /// <summary>
        /// Paddling instead of walking (swim R&D, plan §11.1): the Swim animation, a sunk and nose-up body, slower
        /// and no sprint, jump, emotes or paw. Owner writes, everyone reads. No water yet: <c>?swim=1</c> on the
        /// web or F9 in the editor turns it on anywhere.
        /// </summary>
        [Networked]
        public NetworkBool Swimming { get; set; }

        /// <summary>
        /// Glasses of wine still in effect (GDD §4.3): the owner counts the ones drunk in the last
        /// <see cref="DrinkDuration"/>. Everyone reads it for the sway; the owner for the visuals and the bonus.
        /// </summary>
        [Networked]
        public byte DrinkCount { get; set; }

        /// <summary>The ticks the last <see cref="MaxDrinks"/> glasses were finished at (0 = empty slot), on the network clock like the cigar.</summary>
        [Networked, Capacity(MaxDrinks)]
        NetworkArray<int> DrinkTicks => default;

        /// <summary>Glasses at which a cat is drunk, and how long each glass counts. Not saved: a reload sobers you.</summary>
        public const int DrunkAt = 3;
        public const float DrinkDuration = 360f;
        /// <summary>Glasses remembered at once; a ninth replaces the oldest.</summary>
        const int MaxDrinks = 8;
        const float DrinkRecountInterval = 1f;

        /// <summary>Raised on every client when the name or fur color changes.</summary>
        public event Action AppearanceChanged;

        /// <summary>Owner only: crossed the drunk line either way.</summary>
        public event Action<bool> DrunkChanged;

        public Transform NameplateAnchor => _nameplateAnchor != null ? _nameplateAnchor : transform;
        public Color Swatch => _palette.Get(FurIndex).Swatch;
        public bool IsLocal => Object != null && Object.HasStateAuthority;
        public PlayerRef Owner => Object != null ? Object.StateAuthority : PlayerRef.None;
        public CatReactions Reactions => _reactions;
        public CatAnimatorDriver AnimatorDriver => _animatorDriver;
        public SessionWinnings Winnings { get; private set; }
        public bool IsCigarLit => CigarUntilTick > 0 && Runner != null && Runner.Tick.Raw < CigarUntilTick;
        public bool IsDrunk => DrinkCount >= DrunkAt;

        /// <summary>The local cat has spawned and is drunk.</summary>
        public static bool LocalIsDrunk => Local != null && Local.Object != null && Local.Object.IsValid && Local.IsDrunk;

        const float RttUpdateInterval = 1f;
        const int RttChangeMs = 10;
        /// <summary>How long a victory cigar burns.</summary>
        const float CigarSeconds = 300f;

        CatReactions _reactions;
        CatAnimatorDriver _animatorDriver;
        CatCigar _cigar;
        float _nextRttUpdate;
        float _nextDrinkRecount;

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
            _cigar = GetComponent<CatCigar>();
        }

        public override void Render()
        {
            if (_animatorDriver != null)
            {
                _animatorDriver.Sitting = IsSeated;
                _animatorDriver.Swimming = Swimming;
            }

            if (_cigar != null)
                _cigar.SetLit(IsCigarLit);

            if (HasStateAuthority)
            {
                UpdateRtt();
                if (Application.isEditor && GameInput.KeyPressed(Key.F9))
                    Swimming = !Swimming;
                if (Time.time >= _nextDrinkRecount)
                    RecountDrinks();
            }
        }

        // ---------------------------------------------------------------- Wine (GDD §4.3)

        /// <summary>Owner only: a glass has just been finished at the bar.</summary>
        public void AddDrink()
        {
            if (!HasStateAuthority)
                return;
            var oldest = 0;
            for (var i = 1; i < MaxDrinks; i++)
            {
                if (DrinkTicks[i] < DrinkTicks[oldest])
                    oldest = i;
            }

            DrinkTicks.Set(oldest, Runner.Tick.Raw);
            RecountDrinks();
        }

        /// <summary>Drops the glasses that have worn off and publishes the count; about once a second.</summary>
        void RecountDrinks()
        {
            _nextDrinkRecount = Time.time + DrinkRecountInterval;
            var count = (byte)0;
            for (var i = 0; i < MaxDrinks; i++)
            {
                var tick = DrinkTicks[i];
                if (tick != 0 && (Runner.Tick.Raw - tick) * Runner.DeltaTime < DrinkDuration)
                    count++;
            }

            if (count == DrinkCount)
                return;

            var wasDrunk = IsDrunk;
            DrinkCount = count;
            if (IsDrunk == wasDrunk)
                return;
            if (IsDrunk)
                Announce($"{DisplayName} has had one too many.");
            DrunkChanged?.Invoke(IsDrunk);
        }

        /// <summary>
        /// Owner only: a huge win (<see cref="CatCigar"/> spots them) lights a victory cigar for everyone to see, or
        /// keeps one that's already lit burning for another five minutes. The chat line and the toast go out only
        /// for a fresh light (UX spec §1.6).
        /// </summary>
        public void LightCigar()
        {
            if (!HasStateAuthority || Runner == null)
                return;
            var wasLit = IsCigarLit;
            CigarUntilTick = Runner.Tick.Raw + Mathf.RoundToInt(CigarSeconds / Runner.DeltaTime);
            if (wasLit)
                return;
            Announce($"{DisplayName} lights a victory cigar.");
            Toasts.Show("Big win! You light a victory cigar.");
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
            if (IsLocal && !IsSeated && !Swimming && emote != CatEmote.None)
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
                _cigar?.Bind(this);
                Swimming = Platform.QueryParameter("swim") == "1";
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
                _cigar?.Unbind();
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
