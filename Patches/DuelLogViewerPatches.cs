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
    // ── Duel Log Viewer ──────────────────────────────────────────────

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.Open))]
    class PatchDuelLogOpen
    {
        [HarmonyPostfix]
        static void Postfix(DuelLogController __instance)
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                DuelState.IsDuelLogOpen = true;

                // Capture controller + scroll view for index polling and team lookup
                DuelState.LogController = __instance;
                var sv = __instance.m_ScrollView;
                DuelState.LogScrollView = sv;

                Log.Write("[DuelLog] Opened");
                Speech.SayImmediate("Duel Log");
                DuelLogReader.InitScroll(sv);
            }
            catch (Exception ex) { Log.Write($"[PatchDuelLogOpen] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.Close))]
    class PatchDuelLogClose
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                if (!DuelState.IsDuelLogOpen) return;
                DuelState.IsDuelLogOpen = false;
                Log.Write("[DuelLog] Closed");
            }
            catch (Exception ex) { Log.Write($"[PatchDuelLogClose] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CardInfo), nameof(CardInfo.SetCardByCardId))]
    class PatchDuelLogSetCard
    {
        [HarmonyPostfix]
        static void Postfix(int cardid)
        {
            try
            {
                if (!DuelState.IsDuelLogOpen) return;
                if (cardid <= 0) return;

                // Look up team from the log controller's card name data
                bool isOpponent = false;
                try
                {
                    var controller = DuelState.LogController;
                    if (controller != null)
                    {
                        var cardList = controller.m_DataList_ShowCardName;
                        if (cardList != null)
                        {
                            for (int i = cardList.Count - 1; i >= 0; i--)
                            {
                                if (cardList[i].cardid == cardid)
                                {
                                    isOpponent = DuelState.IsOpponentTeam(cardList[i].team);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"[DuelLogSetCard] Team lookup: {ex.Message}"); }

                // Speak card name only (card info panel shows full details)
                string cardName = DuelLogHelper.ResolveCardName(cardid);
                string speech = isOpponent ? $"Opponent: {cardName}" : cardName;
                Log.Write($"[DuelLog-Card] {speech}");
                Speech.SayItem(speech);
            }
            catch (Exception ex) { Log.Write($"[PatchDuelLogSetCard] {ex.Message}"); }
        }
    }

    /// <summary>
    /// Patches OnUpdateShow* methods on DuelLogController.
    /// These fire when the game renders/updates items in the log scroll view —
    /// including during user scrolling. Each receives the EOM GameObject
    /// containing the rendered text for that entry.
    /// </summary>
    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowAction))]
    class PatchDuelLogShowAction
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "Action", dataindex);
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowCardName))]
    class PatchDuelLogShowCardName
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "CardName", dataindex);
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowPhase))]
    class PatchDuelLogShowPhase
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "Phase", dataindex);
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowTurn))]
    class PatchDuelLogShowTurn
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "Turn", dataindex);
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowText))]
    class PatchDuelLogShowText
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "Text", dataindex);
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.OnUpdateShowChain))]
    class PatchDuelLogShowChain
    {
        [HarmonyPostfix]
        static void Postfix(UnityEngine.GameObject eom, int dataindex)
        {
            DuelLogReader.CaptureItem(eom, "Chain", dataindex);
        }
    }

    /// <summary>
    /// Captures and reads duel log entries. OnUpdateShow* callbacks store
    /// rendered text as entries are created during gameplay. When the log
    /// opens, reads the stored entries. Selection polling reads the
    /// currently selected entry as the user D-pads through the log.
    /// </summary>
    static class DuelLogReader
    {
        private static readonly List<string> _entries = new();
        private static string _lastSpoken = "";
        private static Il2CppYgomSystem.UI.SelectionItem _lastSelectedItem;

        /// <summary>
        /// Capture text from an OnUpdateShow* callback.
        /// Called during gameplay as log entries are created.
        /// Stores the rendered text for later reading.
        /// </summary>
        public static void CaptureItem(UnityEngine.GameObject eom, string type, int dataindex)
        {
            try
            {
                if (eom == null) return;
                var results = TextExtractor.ExtractAll(eom);
                if (results.Count == 0) return;
                string text = string.Join(", ", results.ConvertAll(r => r.Text));
                if (string.IsNullOrWhiteSpace(text)) return;

                _entries.Add(text);
                Log.Write($"[DuelLog-Item] [{type}:{dataindex}] {text}");
            }
            catch (Exception ex) { Log.Write($"[DuelLog-Item] {ex.Message}"); }
        }

        /// <summary>
        /// Read recent log entries when the log opens.
        /// Speaks the last several captured entries.
        /// </summary>
        public static void ReadRecentEntries()
        {
            try
            {
                if (_entries.Count == 0)
                {
                    Speech.SayQueued("No entries");
                    Log.Write("[DuelLog-Read] No entries");
                    return;
                }

                // Read the last few entries (most recent actions)
                int start = Math.Max(0, _entries.Count - 8);
                var recent = new List<string>();
                for (int i = start; i < _entries.Count; i++)
                    recent.Add(_entries[i]);

                string combined = string.Join(". ", recent);
                Log.Write($"[DuelLog-Read] {combined}");
                Speech.SayQueued(combined);
            }
            catch (Exception ex) { Log.Write($"[DuelLog-Read] {ex.Message}"); }
        }

        /// <summary>
        /// Initialize selection tracking when the log opens.
        /// </summary>
        public static void InitScroll(DuelLogScrollView sv)
        {
            _lastSelectedItem = null;
            _lastSpoken = "";
            Log.Write("[DuelLog] Selection tracking initialized");
        }

        /// <summary>
        /// Poll the selector's selected item each frame while the log is open.
        /// When D-pad changes the selection, reads the newly selected entry.
        /// The SelectionItem itself has no text — the visual content is in a
        /// sibling or parent element, so we search up the hierarchy.
        /// Called from BlindDuelCore.Update().
        /// </summary>
        public static void PollSelection()
        {
            try
            {
                var sv = DuelState.LogScrollView;
                if (sv == null) return;

                var selector = sv.selector;
                if (selector == null) return;

                var selected = selector.GetSelectedItem();
                if (selected == null) return;

                // Same item — no change
                if (selected == _lastSelectedItem) return;
                _lastSelectedItem = selected;

                // Clear dedup on every selection change so returning to a
                // previously-spoken entry still reads it
                _lastSpoken = "";

                var go = selected.gameObject;
                if (go == null) return;

                string text = null;
                var itemName = go.name;

                // LP face icons: read individual player LP from Engine
                if (itemName == "FaceIconL" || itemName == "FaceIconR")
                {
                    int player = itemName == "FaceIconL" ? 0 : 1;
                    int lp = DuelClient.GetLP(player);
                    string label = DuelState.IsMyPlayer(player) ? "Your" : "Opponent";
                    text = $"{label} LP: {lp}";
                }

                // Card entries (Card0/Card1/...) are handled by PatchDuelLogSetCard
                // via CardInfo.SetCardByCardId — skip here
                if (text == null && itemName.StartsWith("Card") && go.transform.parent?.name == "CardRoot")
                    return;

                // Try the item's text, then parent, then grandparent
                if (text == null)
                {
                    var transform = go.transform;
                    var results = TextExtractor.ExtractAll(go);
                    if (results.Count > 0)
                        text = string.Join(", ", results.ConvertAll(r => r.Text));

                    if (string.IsNullOrWhiteSpace(text) && transform.parent != null)
                    {
                        results = TextExtractor.ExtractAll(transform.parent.gameObject);
                        if (results.Count > 0)
                            text = string.Join(", ", results.ConvertAll(r => r.Text));
                    }

                    if (string.IsNullOrWhiteSpace(text) && transform.parent?.parent != null)
                    {
                        results = TextExtractor.ExtractAll(transform.parent.parent.gameObject);
                        if (results.Count > 0)
                            text = string.Join(", ", results.ConvertAll(r => r.Text));
                    }
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    Log.Write($"[DuelLog-Poll] No text: {itemName}");
                    return;
                }

                _lastSpoken = text;
                Log.Write($"[DuelLog-Nav] {text}");
                Speech.SayItem(text);
            }
            catch (Exception ex) { Log.Write($"[DuelLog-Poll] {ex.Message}"); }
        }

        public static void Reset()
        {
            _entries.Clear();
            _lastSpoken = "";
            _lastSelectedItem = null;
        }
    }
}
