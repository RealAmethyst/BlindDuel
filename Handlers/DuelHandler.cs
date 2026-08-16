using System;
using Il2CppYgomGame.Duel;
using Il2CppYgomSystem.UI;

namespace BlindDuel
{
    public class DuelHandler : IMenuHandler
    {
        // Dedup: OnSelected fires 3x during list setup for the same button.
        // Track by button reference so two copies of the same card (different
        // buttons, same cardid) both read, but the 3 setup fires are caught.
        private static SelectionButton _lastSelButton;

        /// <summary>Reset selection dedup when a new list opens.</summary>
        public static void ResetSelectionDedup() => _lastSelButton = null;

        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "DuelClient" or "DuelLive";

        public bool OnScreenEntered(string viewControllerName)
        {
            Speech.AnnounceScreen("Duel");
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            // Suppress button speech during duel end animation
            if (DuelState.IsShowingResult) return "";
            if (!NavigationState.IsInDuel) return null;

            // Card selection list (graveyard, chain, extra deck summon, etc.)
            // Read card data directly from ListCard.m_CardData — no SetDescriptionArea needed.
            try
            {
                var csl = button.GetComponentInParent<CardSelectionList>();
                if (csl != null)
                {
                    var listCard = button.GetComponent<ListCard>();
                    if (listCard == null)
                        listCard = button.GetComponentInParent<ListCard>();

                    if (listCard != null)
                    {
                        try
                        {
                            var data = listCard.m_CardData;
                            if (data != null && data.cardid > 0)
                            {
                                // Dedup: OnSelected fires 3 times during list setup
                                // (initial focus, priority recalc, delayed settle).
                                // All fire on the same button — skip if same instance.
                                // Two copies of the same card are different buttons,
                                // so both read correctly.
                                if (button == _lastSelButton)
                                    return "";
                                _lastSelButton = button;

                                // Don't reveal cards the player doesn't know about
                                // (opponent's face-down cards in target selection lists)
                                try
                                {
                                    if (!data.isknown)
                                    {
                                        string index = GetSelectionIndex(button);
                                        string msg = index != null ? $"Face-down card, {index}" : "Face-down card";
                                        bool queued = PatchCardSelectionListSetTitle.ConsumeQueuedFlag();
                                        if (queued) Speech.SayQueued(msg);
                                        else Speech.SayItem(msg);
                                        return "";
                                    }
                                }
                                catch { }

                                // CardSelectionList = efeito solicitando seleção (Enter/A).
                                // Nesses casos (invocar do extra deck, ativar efeito do cemitério,
                                // selecionar card por efeito) sempre lê o card completo.
                                string index2 = GetSelectionIndex(button);
                                bool queued2 = PatchCardSelectionListSetTitle.ConsumeQueuedFlag();
                                CardReader.SpeakCardFromData(data.cardid, index2, queued: queued2);
                                return "";
                            }
                        }
                        catch (Exception ex) { Log.Write($"[DuelHandler] ListCard read: {ex.Message}"); }

                        // m_CardData empty — fall through to zone browse fallback below
                    }
                    else
                    {
                        // No ListCard (cancel/confirm button) — reset card dedup so
                        // navigating back to a card re-reads it.
                        _lastSelButton = null;
                        return null;
                    }
                }
            }
            catch (Exception ex) { Log.Write($"[DuelHandler] CardSelection: {ex.Message}"); }

            // Zone browse fallback (Graveyard, Extra Deck, Banished opened with X).
            // Uses a running counter (DuelState.BrowseIndex) adjusted by BrowseDirection
            // (+1 for down, -1 for up) because the scroll view recycles ~7 template
            // slots, making sibling position unreliable for lists longer than 7 cards.
            if (DuelState.LastBrowsePosition >= 0)
            {
                try
                {
                    // Confirm this is a browse list button (template(Clone) ancestor)
                    if (!IsBrowseListButton(button))
                    {
                        // Not in a browse list — skip fallback for unrelated buttons
                    }
                    else
                    {
                        int player = DuelState.LastBrowsePlayer;
                        int position = DuelState.LastBrowsePosition;
                        int totalCards = 0;
                        try { totalCards = Engine.GetCardNum(player, position); }
                        catch { }

                        // Apply direction to get the new logical index
                        int dir = DuelState.BrowseDirection;
                        int logicalIdx = DuelState.BrowseIndex + dir;

                        // Clamp to valid range
                        if (logicalIdx < 0) logicalIdx = 0;
                        if (totalCards > 0 && logicalIdx >= totalCards)
                            logicalIdx = totalCards - 1;

                        DuelState.BrowseIndex = logicalIdx;

                        // Suppress duplicate reads at list boundaries
                        if (logicalIdx == DuelState.LastBrowseLogicalIdx)
                            return "";
                        DuelState.LastBrowseLogicalIdx = logicalIdx;

                        string indexStr = totalCards > 0 ? $"{logicalIdx + 1} of {totalCards}" : null;
                        string zoneLabel = BuildPileZoneLabel(player, position, indexStr);

                        // Don't reveal opponent's face-down cards
                        if (!DuelState.IsMyPlayer(player))
                        {
                            try
                            {
                                if (!Engine.GetCardFace(player, position, logicalIdx))
                                {
                                    string msg = zoneLabel != null ? $"Face-down card, {zoneLabel}" : "Face-down card";
                                    DuelState.CardDetailLines = null;
                                    DuelState.CardDetailIndex = 0;
                                    Speech.SayItem(msg);
                                    return "";
                                }
                            }
                            catch { }
                        }

                        int mrk = Engine.GetCardID(player, position, logicalIdx);
                        if (mrk > 0)
                        {
                            SpeakCardSummaryFromData(mrk, zoneLabel);
                            return "";
                        }
                        else
                        {
                            string msg = zoneLabel != null ? $"Face-down card, {zoneLabel}" : "Face-down card";
                            DuelState.CardDetailLines = null;
                            DuelState.CardDetailIndex = 0;
                            Speech.SayItem(msg);
                            return "";
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"[DuelHandler] Zone browse fallback: {ex.Message}"); }
            }

            string name = button.name;

            // Field anchors — onFocusFieldHandler handles card + zone reading
            if (name.Contains("Anchor_")) return "";

            // Suppress card speech when the button isn't interactable.
            // The game sets interactable=false during animations/transitions
            // and true when the player can actually interact.
            try { if (!button.interactable) return ""; }
            catch { }

            // Hand cards are handled by PatchHandCardSelect (SelectByViewIndex patch).
            // Action buttons (Summon, Set, etc.) — default behavior.
            return null;
        }

        private static void SpeakCardSummaryFromData(int mrk, string zoneLabel, bool queued = false)
        {
            BlindDuelCore.Preview.Clear();
            var card = CardReader.ReadCardFromData(mrk);

            var lines = card.GetDetailLines(out string summary, zone: zoneLabel);
            DuelState.CardDetailLines = lines;
            DuelState.CardDetailIndex = 0;

            if (string.IsNullOrWhiteSpace(summary))
                summary = !string.IsNullOrWhiteSpace(zoneLabel) ? zoneLabel : card.Name;

            if (string.IsNullOrWhiteSpace(summary)) return;

            if (queued)
                Speech.SayQueued(summary);
            else
                Speech.SayItem(summary);
        }

        private static string BuildPileZoneLabel(int player, int position, string index)
        {
            string side = DuelState.IsMyPlayer(player) ? "" : "Opponent's ";
            string zone = null;
            if (position == Engine.PosGrave)
                zone = $"{side}Graveyard";
            else if (position == Engine.PosExclude)
                zone = $"{side}Banished";
            else if (position == Engine.PosExtra)
                zone = $"{side}Extra Deck";

            if (string.IsNullOrEmpty(zone))
                return index;

            return !string.IsNullOrEmpty(index) ? $"{zone}, {index}" : zone;
        }

        /// <summary>
        /// Get the position of a button among its active siblings with SelectionButton components.
        /// Returns "X of Y" string, or null if it can't be determined.
        /// </summary>
        private static string GetSelectionIndex(SelectionButton button)
        {
            try
            {
                var parent = button.transform.parent;
                if (parent == null) return null;

                int current = -1, total = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (!child.gameObject.activeInHierarchy) continue;
                    if (child.GetComponent<SelectionButton>() == null) continue;
                    if (child == button.transform)
                        current = total;
                    total++;
                }

                if (current >= 0 && total > 0)
                    return $"{current + 1} of {total}";
            }
            catch (Exception ex) { Log.Write($"[DuelHandler] SelectionIndex: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Check if button is inside a browse list (template(Clone) under Content,
        /// or DuelListCard wrapper).
        /// </summary>
        private static bool IsBrowseListButton(SelectionButton button)
        {
            try
            {
                var current = button.transform;
                for (int i = 0; i < 6 && current != null; i++)
                {
                    if (current.name.Contains("DuelListCard"))
                        return true;
                    if (current.name == "template(Clone)"
                        && current.parent != null
                        && current.parent.name == "Content")
                        return true;
                    current = current.parent;
                }
            }
            catch { }
            return false;
        }
    }
}
