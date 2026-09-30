using CasinoMares.Blackjack;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>A UI card that shows one cell of the runtime card atlas (or the card back).</summary>
    public class CardElement : VisualElement
    {
        const float Scale = 2f;
        const int AtlasWidth = 1024;
        const int AtlasHeight = 512;

        public CardElement()
        {
            style.width = CardFaces.CellWidth * Scale;
            style.height = CardFaces.CellHeight * Scale;
            style.marginLeft = style.marginRight = 6;
            style.backgroundImage = Background.FromTexture2D(CardFaces.Atlas);
            style.backgroundSize = new BackgroundSize(new Length(AtlasWidth * Scale), new Length(AtlasHeight * Scale));
            pickingMode = PickingMode.Ignore;
            SetCard(Cards.None);
        }

        /// <summary>Show a card, or the back for <see cref="Cards.None"/>.</summary>
        public void SetCard(byte card)
        {
            var column = card == Cards.None ? 0 : Cards.Rank(card);
            var row = card == Cards.None ? 4 : Cards.Suit(card);
            style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-column * CardFaces.CellWidth * Scale));
            style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-row * CardFaces.CellHeight * Scale));
        }
    }
}
