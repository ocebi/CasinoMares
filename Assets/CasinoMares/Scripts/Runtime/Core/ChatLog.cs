using System;
using System.Collections.Generic;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>Local store of chat and system messages (GDD §9.5). Late joiners don't get history.</summary>
    public static class ChatLog
    {
        public const int MaxMessages = 60;
        public const int MaxMessageLength = 140;

        public readonly struct Message
        {
            public readonly string Sender;
            public readonly Color SenderColor;
            public readonly string Text;
            public readonly bool IsSystem;

            public Message(string sender, Color senderColor, string text, bool isSystem)
            {
                Sender = sender;
                SenderColor = senderColor;
                Text = text;
                IsSystem = isSystem;
            }
        }

        static readonly List<Message> _messages = new();

        public static IReadOnlyList<Message> Messages => _messages;

        public static event Action<Message> Added;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _messages.Clear();
            Added = null;
        }

        public static void AddPlayer(string sender, Color color, string text) => Add(new Message(sender, color, text, false));

        public static void AddSystem(string text) => Add(new Message(null, default, text, true));

        public static string Sanitize(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length > MaxMessageLength ? text.Substring(0, MaxMessageLength) : text;
        }

        static void Add(Message message)
        {
            _messages.Add(message);
            if (_messages.Count > MaxMessages)
                _messages.RemoveAt(0);
            Added?.Invoke(message);
        }
    }
}
