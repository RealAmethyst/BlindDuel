using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppYgomGame.Card;
using Il2CppYgomGame.Duel;
using Il2CppYgomGame.MDMarkup;
using Il2CppYgomGame.Tutorial;
using Il2CppYgomSystem.UI;
using HarmonyLib;

namespace BlindDuel
{
    /// <summary>
    /// Speaks the selection prompt for card selection list mode.
    /// SetTitle is called internally during SetListImpl for list-based selections.
    /// Shared dedup/speech logic lives in HandleTitle().
    /// </summary>
    [HarmonyPatch(typeof(CardSelectionList), nameof(CardSelectionList.SetTitle))]
    class PatchCardSelectionListSetTitle
    {
        private static bool _nextReadQueued;
        private static string _lastTitle = "";

        /// <summary>
        /// Shared handler for selection prompt titles from both SetTitle and SetList patches.
        /// Deduplicates and speaks the title.
        /// </summary>
        public static void HandleTitle(string cleaned)
        {
            if (cleaned == _lastTitle) return;
            _lastTitle = cleaned;

            Log.Write($"[CardSelectionList] {cleaned}");
            DuelState.MessageJustAnnounced = true;
            Speech.SayImmediate(cleaned);
            _nextReadQueued = true;

            // Queue the deferred or interrupted item after the title.
            // This mirrors QueueFocusedItem for normal menus.
            DuelState.HasPendingSelection = false;
            QueueDeferredItem();

            // Clear last field focus — the selection list replaces the field
            // context, so re-queuing the last card would sound like a menu item.
            FieldFocusHandler.ClearLastFocus();

            // Re-queue button text that was queued but then interrupted by SayImmediate
            if (!string.IsNullOrEmpty(DuelState.LastQueuedButtonText))
            {
                Speech.SayQueued(DuelState.LastQueuedButtonText);
                DuelState.LastQueuedButtonText = null;
            }
        }

        /// <summary>
        /// Speak the item that was deferred during selection setup, queued after the title.
        /// </summary>
        private static void QueueDeferredItem()
        {
            // Deferred button (card list selection)
            var btn = DuelState.DeferredSelectionButton;
            if (btn != null)
            {
                DuelState.DeferredSelectionButton = null;
                try
                {
                    var handler = HandlerRegistry.Current;
                    if (handler != null)
                    {
                        string text = handler.OnButtonFocused(btn);
                        // Handler spoke the card directly (returned "")
                        // or returned null — try default text
                        if (text == null)
                        {
                            text = TextExtractor.ExtractFirst(btn.gameObject);
                        }
                        if (!string.IsNullOrWhiteSpace(text) && text != "")
                            Speech.SayQueued(text);
                    }
                }
                catch (Exception ex) { Log.Write($"[QueueDeferred] Button: {ex.Message}"); }
                return;
            }

            // Deferred field focus (field zone selection)
            var focus = DuelState.DeferredFieldFocus;
            if (focus.HasValue)
            {
                DuelState.DeferredFieldFocus = null;
                try
                {
                    var (player, position, viewIndex) = focus.Value;
                    FieldFocusHandler.SpeakDeferredFocus(player, position, viewIndex);
                }
                catch (Exception ex) { Log.Write($"[QueueDeferred] Field: {ex.Message}"); }
            }
        }

        [HarmonyPostfix]
        static void Postfix(string title)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title)) return;
                string cleaned = TextUtil.StripTags(title);
                if (string.IsNullOrWhiteSpace(cleaned)) return;
                HandleTitle(cleaned);
            }
            catch (Exception ex) { Log.Write($"[PatchCardSelectionListSetTitle] {ex.Message}"); }
        }

        public static bool ConsumeQueuedFlag()
        {
            bool val = _nextReadQueued;
            _nextReadQueued = false;
            return val;
        }

        public static void ResetDedup() => _lastTitle = "";
    }

    /// <summary>
    /// Catches selection prompts that bypass SetTitle — e.g. field selection mode
    /// ("Select the card to send to the Graveyard"). SetList receives the title
    /// as a parameter and may not call SetTitle for all selection modes.
    /// </summary>
    [HarmonyPatch(typeof(CardSelectionList), nameof(CardSelectionList.SetList))]
    class PatchCardSelectionListSetList
    {
        /// <summary>
        /// PREFIX: Set pending flag BEFORE the game creates items and auto-focuses.
        /// Mirrors HasPendingScreen for normal menus.
        /// </summary>
        [HarmonyPrefix]
        static void Prefix()
        {
            if (NavigationState.IsInDuel)
            {
                DuelState.HasPendingSelection = true;
                DuelState.DeferredSelectionButton = null;
                DuelState.DeferredFieldFocus = null;
                DuelHandler.ResetSelectionDedup();
                PatchCardSelectionListSetTitle.ResetDedup();
            }
        }

        [HarmonyPostfix]
        static void Postfix(string title)
        {
            try
            {
                // Always clear pending flag — even if title is empty.
                // Otherwise HasPendingSelection stays true and all buttons are deferred forever.
                if (NavigationState.IsInDuel)
                    DuelState.HasPendingSelection = false;

                if (string.IsNullOrWhiteSpace(title)) return;
                string cleaned = TextUtil.StripTags(title);
                if (string.IsNullOrWhiteSpace(cleaned)) return;
                PatchCardSelectionListSetTitle.HandleTitle(cleaned);
            }
            catch (Exception ex) { Log.Write($"[PatchCardSelectionListSetList] {ex.Message}"); }
        }
    }

    /// <summary>
    /// Catches field selection prompts that go through EffectTaskRunDialog
    /// (e.g. "Select the card to send to the Graveyard"). RunDialog sets up
    /// the selection UI and populates the text. Reading __instance.text here
    /// catches prompts that bypass CardSelectionList.SetTitle/SetList.
    /// </summary>
    [HarmonyPatch(typeof(EffectTaskRunDialog), nameof(EffectTaskRunDialog.RunDialog))]
    class PatchEffectTaskRunDialog
    {
        [HarmonyPostfix]
        static void Postfix(EffectTaskRunDialog __instance)
        {
            try
            {
                if (!NavigationState.IsInDuel) return;

                string text = __instance.text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string cleaned = TextUtil.StripTags(text);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        Log.Write($"[EffectTaskRunDialog] {cleaned}");
                        PatchCardSelectionListSetTitle.HandleTitle(cleaned);
                        return;
                    }
                }

                // Fallback: read activateCardSelectionText static field
                string actText = EffectTaskRunDialog.activateCardSelectionText;
                if (!string.IsNullOrWhiteSpace(actText))
                {
                    string cleaned = TextUtil.StripTags(actText);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        Log.Write($"[EffectTaskRunDialog.activate] {cleaned}");
                        PatchCardSelectionListSetTitle.HandleTitle(cleaned);
                    }
                }
            }
            catch (Exception ex) { Log.Write($"[PatchEffectTaskRunDialog] {ex.Message}"); }
        }
    }

}
