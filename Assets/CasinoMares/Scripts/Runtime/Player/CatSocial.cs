using CasinoMares.Core;
using Fusion;

namespace CasinoMares.Player
{
    /// <summary>
    /// A player cat's broadcasts: chat lines and announcements (GDD §9.5), emotes and paw swipes, and chip gifts
    /// (GDD §7.3). Sits next to <see cref="PlayerCat"/> on the player prefab and reads its name, colour and state;
    /// reach it through <see cref="PlayerCat.Social"/>.
    /// </summary>
    public class CatSocial : NetworkBehaviour
    {
        PlayerCat _cat;

        void Awake()
        {
            _cat = GetComponent<PlayerCat>();
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
            ChatLog.AddPlayer(_cat.DisplayName.ToString(), _cat.Swatch, text);
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
            if (_cat.IsLocal && !_cat.IsSeated && !_cat.Swimming && emote != CatEmote.None)
                RPC_Emote((byte)emote);
        }

        /// <summary>Show a paw swipe on this cat for everyone.</summary>
        public void PlaySwipe()
        {
            if (_cat.IsLocal)
                RPC_Swipe();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Emote(byte emote) => _cat.Reactions?.PlayEmote((CatEmote)emote);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Swipe() => _cat.Reactions?.PlaySwipe();

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
    }
}
