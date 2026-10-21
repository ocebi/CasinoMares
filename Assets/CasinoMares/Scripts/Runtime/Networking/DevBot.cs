using System;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Networking
{
    /// <summary>
    /// Test helper for multiplayer checks without a second human: a build started with <c>-bot</c> connects on its
    /// own, sits at a blackjack table, bets the minimum and plays basic strategy. Optional arguments:
    /// <c>-botName Name</c>, <c>-botTable 2</c>, <c>-botSeat 0</c>. Use together with <c>-profile bot</c> so it
    /// doesn't touch the real profile file.
    /// </summary>
    public class DevBot : MonoBehaviour
    {
        string _tableName = "Table 2";
        int _seat;
        float _nextAction;
        bool _greeted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-bot") < 0 || FindAnyObjectByType<DevBot>() != null)
                return;
            DontDestroyOnLoad(new GameObject("DevBot").AddComponent<DevBot>().gameObject);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        void Start()
        {
            _tableName = $"Table {Arg("-botTable", "2")}";
            _seat = int.TryParse(Arg("-botSeat", "0"), out var seat) ? seat : 0;
            ProfileStore.SetIdentity(Arg("-botName", "BotCat"), UnityEngine.Random.Range(0, 6));
            if (ProfileStore.Current.Balance < BlackjackRules.MinBet)
                ProfileStore.SetBalance(1000);
            Application.targetFrameRate = 30;
        }

        void Update()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null)
                return;

            if (connection.State is ConnectionState.Offline or ConnectionState.Failed)
            {
                if (Time.time > _nextAction)
                {
                    _nextAction = Time.time + 5f;
                    connection.Connect();
                }

                return;
            }

            var me = PlayerCat.Local;
            if (me == null || Time.time < _nextAction)
                return;
            _nextAction = Time.time + 0.5f;

            if (!_greeted)
            {
                _greeted = true;
                me.Social.SendChat("Beep boop, I'm a test cat. Deal me in!");
            }

            var table = BlackjackTable.All.Find(t => t.DisplayName == _tableName);
            if (table == null || table.Object == null || !table.Object.IsValid)
                return;

            if (!table.TryGetSeatOf(table.Runner.LocalPlayer, out var seat))
            {
                table.RequestSeat(_seat);
                return;
            }

            if (table.Phase == BlackjackPhase.Betting && !table.IsLockedAt(seat) && ProfileStore.Current.Balance >= BlackjackRules.MinBet)
                table.PlaceBet(seat, BlackjackRules.MinBet, true);

            if (table.IsActiveTurn(seat))
            {
                var hand = new byte[table.CardCountAt(seat, table.ActiveHand)];
                for (var i = 0; i < hand.Length; i++)
                    hand[i] = table.CardAt(seat, table.ActiveHand, i);
                var affordable = ProfileStore.Current.Balance >= table.HandBetAt(seat, table.ActiveHand);
                var action = BasicStrategy.Decide(hand, table.DealerCardAt(0), table.CanDouble(seat) && affordable, table.CanSplit(seat) && affordable);
                table.Act(seat, action);
                _nextAction = Time.time + 1.2f;
            }
        }
    }
}
