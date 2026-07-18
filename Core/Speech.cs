using System;
using System.Collections.Generic;
using Il2CppYgomSystem.UI;
using MelonLoader;

namespace BlindDuel
{
    /// <summary>
    /// High-level speech coordination for BlindDuel.
    ///
    /// Speech flow:
    /// 1. Menu/screen announcement → Say() (interrupts) — only actual visible game text
    /// 2. Current focused item → SayQueued() — queues after announcement
    /// 3. Navigation between items → Say() with item text, then SayQueued() for index
    /// 4. Item description/help → SayQueued() after item name
    /// </summary>
    public static class Speech
    {
        private static readonly List<string> _history = new();
        private static string _lastSpoken = "";
        private static string _lastHeader = "";
        private static SelectionButton _lastButton;

        public static IReadOnlyList<string> History => _history;

        /// <summary>
        /// Announce a screen or menu change. Interrupts current speech.
        /// Only speak actual visible text from the game data.
        /// </summary>
        public static void AnnounceScreen(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = TextUtil.StripTags(text).Trim();
            if (string.IsNullOrWhiteSpace(text) || text == _lastHeader) return;

            _lastHeader = text;
            _lastSpoken = "";

            Log.Write($"[Screen] {text}");
            MelonLogger.Msg($"screen: {text}");
            ScreenReader.Say(text);
            RecordHistory(text);
        }

        /// <summary>
        /// Speak a menu item when navigating. Interrupts current speech.
        /// </summary>
        public static void SayItem(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = TextUtil.StripTags(text).Trim();
            if (string.IsNullOrWhiteSpace(text) || text == _lastSpoken) return;

            _lastSpoken = text;

            Log.Write($"[Item] {text}");
            MelonLogger.Msg($"item: {text}");
            ScreenReader.Say(text);
            RecordHistory(text);
        }

        /// <summary>
        /// Queue additional info after item (non-interrupting).
        /// Use for supplementary details like LP changes, download progress, etc.
        /// </summary>
        public static void SayQueued(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = TextUtil.StripTags(text).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            Log.Write($"[Queued] {text}");
            MelonLogger.Msg($"queued: {text}");
            ScreenReader.SayQueued(text);
            RecordHistory(text);
        }

        /// <summary>
        /// Speak immediately with interrupt. For urgent info like LP changes.
        /// </summary>
        public static void SayImmediate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = TextUtil.StripTags(text).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            _lastSpoken = text;
            Log.Write($"[Immediate] {text}");
            MelonLogger.Msg($"immediate: {text}");
            ScreenReader.Say(text);
            RecordHistory(text);
        }

        /// <summary>
        /// Speak a single character or short text for typing feedback.
        /// No dedup — must always speak even if the same character is typed twice.
        /// </summary>
        public static void SayTyping(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            ScreenReader.Say(text);
        }

        /// <summary>
        /// Returns true if this button was already spoken for (duplicate fire).
        /// </summary>
        public static bool IsSameButton(SelectionButton button, bool peek = false)
        {
            if (button == _lastButton) return true;
            if (!peek) _lastButton = button;
            return false;
        }

        /// <summary>
        /// Reset text dedup tracking (call when a button is deselected).
        /// Note: _lastButton is NOT reset here — it persists across deselect/reselect
        /// to catch the game's snap-settle pattern (rapid deselect+reselect of same button).
        /// It naturally updates when a different button is selected.
        /// </summary>
        public static void ResetDedup()
        {
            _lastSpoken = "";
        }

        /// <summary>
        /// Reset button dedup so the next focus speaks even if it's the same button.
        /// Used when returning from overlay panels (e.g. CardActionMenu).
        /// </summary>
        public static void ResetButtonDedup()
        {
            _lastButton = null;
        }

        /// <summary>
        /// Reset the header dedup so the next AnnounceScreen speaks even if it
        /// matches the last header. Called when a dialog closes — otherwise the
        /// same dialog re-opening with identical text gets silently suppressed.
        /// </summary>
        public static void ResetHeaderDedup()
        {
            _lastHeader = "";
        }

        /// <summary>
        /// Silence current speech.
        /// </summary>
        public static void Silence()
        {
            ScreenReader.Silence();
        }

        /// <summary>
        /// Repeat the last spoken message.
        /// </summary>
        public static void RepeatLast()
        {
            ScreenReader.RepeatLast();
        }

        private static void RecordHistory(string text)
        {
            _history.Add(text);
        }
    }
}
