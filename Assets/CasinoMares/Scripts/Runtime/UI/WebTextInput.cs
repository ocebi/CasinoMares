using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace CasinoMares.UI
{
    /// <summary>
    /// Typing in a browser. On phones, Unity's own keyboard support focuses a page text box a frame after the tap, and
    /// phones (iPhone Safari above all) only open the keyboard for a focus that happens during the tap itself. On
    /// desktop, a field focused from code (Enter opening the chat) shows as focused but gets no typing. So in a browser
    /// each attached field gets a real page text box laid over it (CasinoMaresWeb.jslib): clicking or tapping it is an
    /// ordinary browser click, the editing is the browser's own (caret, autocorrect, paste, accents), and the text is
    /// copied into the field. The game still draws the field's box; the page draws the text. A field can also name the
    /// button that opens it (the Chat pill), and the page then focuses the box during that button's tap. Outside a
    /// browser this does nothing and the fields work as usual.
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public class WebTextInput : MonoBehaviour
    {
        class Box
        {
            public int Id;
            public VisualElement Field;
            public VisualElement Input;
            public Func<string> Get;
            public Action<string> Set;
            public Action Submitted;
            public Action Cancelled;
            public Func<VisualElement> OpenedBy;
            /// <summary>The field's text when it was last copied either way, to spot the game changing it.</summary>
            public string LastValue;
            public (Rect area, float font, float padLeft, float padRight)? Placed;
            public Rect Armed;
        }

        static readonly Dictionary<string, Box> Boxes = new();
        static WebTextInput _driver;

        /// <summary>Fields get page text boxes: in a browser.</summary>
        public static bool IsActive
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Gives <paramref name="field"/> a page text box (only where <see cref="IsActive"/>). <paramref name="key"/>
        /// names the box, so a field that is built again reuses it. <paramref name="submitted"/> runs on the keyboard's
        /// Enter key and <paramref name="cancelled"/> on Esc (the game doesn't see keys typed into the box);
        /// <paramref name="openedBy"/> returns the button whose tap opens the field, or null while there's none.
        /// </summary>
        public static void Attach(string key, TextField field, Action submitted = null, Action cancelled = null,
            Func<VisualElement> openedBy = null) =>
            Attach(key, field, () => field.value, text => field.value = text, field.maxLength, false, submitted, cancelled,
                openedBy);

        public static void Attach(string key, IntegerField field, Action submitted = null, Action cancelled = null) =>
            Attach(key, field, () => field.value.ToString(CultureInfo.InvariantCulture), text =>
            {
                // A half-typed number (empty) leaves the value alone.
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    field.value = number;
            }, 9, true, submitted, cancelled, null);

        static void Attach(string key, VisualElement field, Func<string> get, Action<string> set, int maxLength, bool numeric,
            Action submitted, Action cancelled, Func<VisualElement> openedBy)
        {
            if (!IsActive)
                return;

            if (_driver == null)
            {
                var driver = new GameObject("WebTextInput");
                DontDestroyOnLoad(driver);
                _driver = driver.AddComponent<WebTextInput>();
            }

            if (!Boxes.TryGetValue(key, out var box))
            {
                box = new Box { Id = Boxes.Count };
                Boxes.Add(key, box);
                CM_TextInputCreate(box.Id, numeric ? 1 : 0, maxLength, submitted != null ? 1 : 0);
            }

            box.Field = field;
            box.Input = field.Q(className: TextInputBaseField<string>.inputUssClassName) ?? field;
            box.Get = get;
            box.Set = set;
            box.Submitted = submitted;
            box.Cancelled = cancelled;
            box.OpenedBy = openedBy;
            box.LastValue = get();
            box.Placed = null;
            box.Armed = default;
            CM_TextInputSetText(box.Id, box.LastValue);
            CM_TextInputHide(box.Id);
            CM_TextInputOpenOnTap(box.Id, 0f, 0f, 0f, 0f);

            // The page draws the text and the caret.
            var text = box.Input.Q<TextElement>();
            if (text != null)
                text.style.color = Color.clear;
        }

        /// <summary>Focuses the field (its page text box where <see cref="IsActive"/>).</summary>
        public static void Focus(VisualElement field)
        {
            if (Find(field) is { } box)
                CM_TextInputFocus(box.Id);
            else
                field.Focus();
        }

        public static void Blur(VisualElement field)
        {
            if (Find(field) is { } box)
                CM_TextInputBlur(box.Id);
            else
                field.Blur();
        }

        /// <summary>Copies the latest typing into the field now, rather than at the start of the next frame.</summary>
        public static void Pull(VisualElement field)
        {
            if (Find(field) is { } box)
                PullText(box);
        }

        static Box Find(VisualElement field)
        {
            foreach (var box in Boxes.Values)
            {
                if (box.Field == field)
                    return box;
            }

            return null;
        }

        static void PullText(Box box)
        {
            var text = CM_TextInputTakeText(box.Id);
            if (text == null)
                return;
            box.Set(text);
            box.LastValue = box.Get();
        }

        // Before the game's scripts, so they see this frame's typing.
        void Update()
        {
            foreach (var box in Boxes.Values)
            {
                PullText(box);
                if (CM_TextInputTakeSubmit(box.Id) != 0)
                    box.Submitted?.Invoke();
                if (CM_TextInputTakeCancel(box.Id) != 0)
                    box.Cancelled?.Invoke();
            }
        }

        // After the game's scripts, which open, close and fill the fields.
        void LateUpdate()
        {
            foreach (var box in Boxes.Values)
            {
                var value = box.Get();
                if (value != box.LastValue)
                {
                    box.LastValue = value;
                    CM_TextInputSetText(box.Id, value);
                }

                if (Shown(box.Input, out var area, out var panelHeight))
                {
                    var style = box.Input.resolvedStyle;
                    var placed = (area, style.fontSize / panelHeight, (style.borderLeftWidth + style.paddingLeft) / panelHeight,
                        (style.borderRightWidth + style.paddingRight) / panelHeight);
                    if (box.Placed != placed)
                    {
                        box.Placed = placed;
                        CM_TextInputPlace(box.Id, area.x, area.y, area.width, area.height, placed.Item2, placed.Item3, placed.Item4);
                    }
                }
                else if (box.Placed != null)
                {
                    box.Placed = null;
                    CM_TextInputHide(box.Id);
                }

                var trigger = box.OpenedBy?.Invoke();
                var armed = trigger != null && Shown(trigger, out var zone, out _) ? zone : default;
                if (armed != box.Armed)
                {
                    box.Armed = armed;
                    CM_TextInputOpenOnTap(box.Id, armed.x, armed.y, armed.width, armed.height);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="element"/> is on screen, and where, as shares of the panel's width and height.
        /// </summary>
        static bool Shown(VisualElement element, out Rect area, out float panelHeight)
        {
            area = default;
            panelHeight = 0f;
            if (element?.panel == null)
                return false;
            for (var e = element; e != null; e = e.parent)
            {
                var style = e.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden || style.opacity < 0.01f)
                    return false;
            }

            var bounds = element.worldBound;
            var size = element.panel.visualTree.layout.size;
            // Also false for NaN, which a field has before its first layout.
            if (!(bounds.width > 1f && bounds.height > 1f && size.x > 1f && size.y > 1f) || float.IsNaN(bounds.x) ||
                float.IsNaN(bounds.y))
                return false;

            area = new Rect(bounds.x / size.x, bounds.y / size.y, bounds.width / size.x, bounds.height / size.y);
            panelHeight = size.y;
            return true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CM_TextInputCreate(int id, int numeric, int maxLength, int submits);
        [DllImport("__Internal")] static extern void CM_TextInputPlace(int id, float x, float y, float width, float height, float font, float padLeft, float padRight);
        [DllImport("__Internal")] static extern void CM_TextInputHide(int id);
        [DllImport("__Internal")] static extern void CM_TextInputOpenOnTap(int id, float x, float y, float width, float height);
        [DllImport("__Internal")] static extern void CM_TextInputSetText(int id, string text);
        [DllImport("__Internal")] static extern string CM_TextInputTakeText(int id);
        [DllImport("__Internal")] static extern int CM_TextInputTakeSubmit(int id);
        [DllImport("__Internal")] static extern int CM_TextInputTakeCancel(int id);
        [DllImport("__Internal")] static extern void CM_TextInputFocus(int id);
        [DllImport("__Internal")] static extern void CM_TextInputBlur(int id);
#else
        static void CM_TextInputCreate(int id, int numeric, int maxLength, int submits) { }
        static void CM_TextInputPlace(int id, float x, float y, float width, float height, float font, float padLeft, float padRight) { }
        static void CM_TextInputHide(int id) { }
        static void CM_TextInputOpenOnTap(int id, float x, float y, float width, float height) { }
        static void CM_TextInputSetText(int id, string text) { }
        static string CM_TextInputTakeText(int id) => null;
        static int CM_TextInputTakeSubmit(int id) => 0;
        static int CM_TextInputTakeCancel(int id) => 0;
        static void CM_TextInputFocus(int id) { }
        static void CM_TextInputBlur(int id) { }
#endif
    }
}
