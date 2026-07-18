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
    [HarmonyPatch(typeof(DuelClient), nameof(DuelClient.Awake))]
    class PatchDuelClientAwake
    {
        [HarmonyPostfix]
        static void Postfix(DuelClient __instance)
        {
            NavigationState.CurrentMenu = Menu.Duel;
            NavigationState.IsInDuel = true;
            DuelLogReader.Reset();

            // Log player identity for debugging online duel perspective
            try
            {
                var init = __instance.engineInitializer;
                if (init != null)
                    Log.Write($"[DuelClientAwake] myPlayerNum={init.myPlayerNum}, rivalPlayerNum={init.rivalPlayerNum}");
            }
            catch { }

            // Subscribe to the game's native field focus event.
            // This fires when the duel cursor moves to any card/zone.
            try
            {
                FieldFocusHandler.Subscribe(__instance);
            }
            catch (Exception ex) { Log.Write($"[DuelClientAwake] Focus subscribe failed: {ex.Message}"); }

            try
            {
                AttackTargetHandler.Subscribe(__instance);
            }
            catch (Exception ex) { Log.Write($"[DuelClientAwake] Attack subscribe failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Handles field/hand/zone card reading via the game's native focus system.
    /// Subscribes to DuelClient.onFocusFieldHandler (delegate subscription, not Harmony patch,
    /// because InvokeFocusField is called from native code and bypasses managed wrappers).
    /// Fires when the duel cursor moves to any position (monsters, spells, hand,
    /// graveyard, extra deck, banished, etc.). Replaces SetDescriptionArea for
    /// all duel field card reading — no more spam from animations or summons.
    /// </summary>
    static class FieldFocusHandler
    {
        private static int _lastUniqueId;

        // Track last focus so dialogs can re-queue the interrupted item
        private static int _lastPlayer, _lastPosition, _lastViewIndex;
        private static bool _hasLastFocus;

        // Hold a reference to prevent GC of the managed delegate
        private static DuelClient.onFocusFieldDelegate _handler;

        public static void Subscribe(DuelClient client)
        {
            _handler = (Action<int, int, int>)OnFieldFocused;
            client.add_onFocusFieldHandler(_handler);
            Log.Write("[FieldFocus] Subscribed to onFocusFieldHandler");
        }

        /// <summary>
        /// Programmatically move focus to a field position and announce it.
        /// Uses DuelFieldBase.SelectItem to move the cursor through the game's
        /// SelectionButton system, then announces via our focus handler.
        /// </summary>
        public static void FocusPosition(int player, int position, int viewIndex = 0)
        {
            // Find the zone's SelectionButton by GameObject name and call Select(false, true)
            try
            {
                string anchorName = GetAnchorName(player, position, viewIndex);
                if (anchorName == null) { goto fallback; }

                var go = UnityEngine.GameObject.Find(anchorName);
                if (go == null) { Log.Write($"[FieldNav] GO not found: {anchorName}"); goto fallback; }

                var btn = go.GetComponent<Il2CppYgomSystem.UI.SelectionButton>();
                if (btn == null) { Log.Write($"[FieldNav] No SelectionButton on {anchorName}"); goto fallback; }

                // Deselect the currently focused item first
                try
                {
                    var allButtons = UnityEngine.Object.FindObjectsOfType<Il2CppYgomSystem.UI.SelectionButton>();
                    foreach (var b in allButtons)
                    {
                        try { if (b.isSelected) b.OnDeselected(); } catch { }
                    }
                }
                catch { }

                // Select the target (CallerCount 267)
                var item = btn.TryCast<Il2CppYgomSystem.UI.SelectionItem>();
                bool ok = item.Select(false, true);
                Log.Write($"[FieldNav] {anchorName} Select={ok}");
                if (ok) return; // game should fire onFocusFieldHandler
            }
            catch (Exception ex) { Log.Write($"[FieldNav] {ex.Message}"); }

            fallback:
            OnFieldFocused(player, position, viewIndex);
        }

        private static string GetAnchorName(int player, int position, int viewIndex)
        {
            string side = DuelState.IsMyPlayer(player) ? "Near" : "Far";

            // Hand cards use different hierarchy
            if (position == Engine.PosHand)
                return $"{side}HandCard/HandCard{viewIndex}/HandCardButton{viewIndex}";

            string zone = null;
            if (position == Engine.PosMonsterLL) zone = "Monster0";
            else if (position == Engine.PosMonsterL) zone = "Monster1";
            else if (position == Engine.PosMonsterC) zone = "Monster2";
            else if (position == Engine.PosMonsterR) zone = "Monster3";
            else if (position == Engine.PosMonsterRR) zone = "Monster4";
            else if (position == Engine.PosMagicLL) zone = "Magic0";
            else if (position == Engine.PosMagicL) zone = "Magic1";
            else if (position == Engine.PosMagicC) zone = "Magic2";
            else if (position == Engine.PosMagicR) zone = "Magic3";
            else if (position == Engine.PosMagicRR) zone = "Magic4";
            else if (position == Engine.PosExLMonster) zone = "ExMonsterL";
            else if (position == Engine.PosExRMonster) zone = "ExMonsterR";
            else if (position == Engine.PosField) zone = "FieldMagic";
            else if (position == Engine.PosGrave) zone = "Grave";
            else if (position == Engine.PosExtra) zone = "Extra";
            else if (position == Engine.PosDeck) zone = "MainDeck";
            else if (position == Engine.PosExclude) zone = "Exclude";

            if (zone == null) return null;
            return $"Anchor_{side}_{zone}";
        }

        static void OnFieldFocused(int player, int position, int viewIndex)
        {
            try
            {
                if (!NavigationState.IsInDuel || !DuelState.HasPhaseStarted) return;
                if (DuelState.IsShowingResult) return;

                // Player left the hand/selection — reset dedup so re-entering reads correctly
                PatchCardInfoSetDescription.ResetHandDedup();
                DuelHandler.ResetSelectionDedup();

                // Selection list pending — defer field focus until title is spoken.
                if (DuelState.HasPendingSelection)
                {
                    DuelState.DeferredFieldFocus = (player, position, viewIndex);
                    return;
                }

                // Suppress while field input is blocked (animations, transitions).
                // fieldInputBlockCounter is the game's native interactivity gate.
                try
                {
                    var client = DuelClient.instance;
                    if (client != null && client.fieldInputBlockCounter > 0) return;
                }
                catch (Exception ex) { Log.Write($"[FocusField] Input block check: {ex.Message}"); }

                // After CardCommand closes, suppress this auto-focus silently.
                // The selection prompt message will speak first, then
                // MessageJustAnnounced will queue the next manual navigation.
                if (DuelState.SuppressNextFieldFocus)
                {
                    DuelState.SuppressNextFieldFocus = false;
                    // Store for re-queue, but skip pile zones (navigation artifacts)
                    if (position != Engine.PosExtra && position != Engine.PosDeck)
                    {
                        _lastPlayer = player;
                        _lastPosition = position;
                        _lastViewIndex = viewIndex;
                        _hasLastFocus = true;
                    }
                    return;
                }

                // Consume announcement flags — queue speech after a game event
                // instead of interrupting it. Also clear screen/dialog flags
                // since duel navigation bypasses ButtonPatches where they'd
                // normally be consumed, causing stale flags to persist.
                bool queued = DuelState.MessageJustAnnounced;
                DuelState.MessageJustAnnounced = false;
                NavigationState.ScreenJustAnnounced = false;
                NavigationState.DialogJustAnnounced = false;

                // Track for re-queue if a dialog interrupts this focus
                _lastPlayer = player;
                _lastPosition = position;
                _lastViewIndex = viewIndex;
                _hasLastFocus = true;

                string zone = GetZoneName(player, position, viewIndex);

                // Track pile zone focus so DuelHandler can look up card IDs
                // when the player opens the zone's card list (X button).
                if (position == Engine.PosGrave || position == Engine.PosExtra || position == Engine.PosExclude)
                {
                    DuelState.LastBrowsePlayer = player;
                    DuelState.LastBrowsePosition = position;
                    DuelState.BrowseIndex = -1;
                    DuelState.BrowseDirection = 1;
                    DuelState.LastBrowseLogicalIdx = -1;
                }
                else
                {
                    // Left the pile zone — clear browse state so the fallback
                    // doesn't fire on unrelated buttons.
                    DuelState.LastBrowsePlayer = -1;
                    DuelState.LastBrowsePosition = -1;
                    DuelState.BrowseIndex = -1;
                    DuelState.BrowseDirection = 1;
                    DuelState.LastBrowseLogicalIdx = -1;
                }

                // Pile zones (Extra Deck, Deck) — just speak the zone name,
                // don't read individual cards from the pile.
                if (position == Engine.PosExtra || position == Engine.PosDeck)
                {
                    if (!string.IsNullOrEmpty(zone))
                        SpeakField(zone, queued);
                    _lastUniqueId = 0;
                    _hasLastFocus = false; // Don't re-queue pile zones after dialogs
                    return;
                }

                int mrk = 0;
                int uniqueId = 0;
                try
                {
                    mrk = Engine.GetCardID(player, position, viewIndex);
                    uniqueId = Engine.GetCardUniqueID(player, position, viewIndex);
                }
                catch (Exception ex)
                {
                    Log.Write($"[FocusField] Engine query failed: {ex.Message}");
                }

                // Opponent's face-down cards: game hides the card ID (mrk=0) but
                // the card still exists. Use GetCardNum to detect it on field zones.
                if (mrk <= 0 && !DuelState.IsMyPlayer(player) && (IsMonsterZone(position) || IsSpellTrapZone(position) || position == Engine.PosHand))
                {
                    try
                    {
                        int count = Engine.GetCardNum(player, position);
                        if (count > 0)
                        {
                            string msg = !string.IsNullOrEmpty(zone)
                                ? $"Face-down card, {zone}"
                                : "Face-down card";
                            Log.Write($"[FocusField] {msg}");
                            SpeakField(msg, queued);
                            DuelState.CardDetailLines = null;
                            DuelState.CardDetailIndex = 0;
                            _lastUniqueId = 0;
                            return;
                        }
                    }
                    catch (Exception ex) { Log.Write($"[FocusField] Face-down check: {ex.Message}"); }
                }

                if (mrk <= 0)
                {
                    // No card at this position — speak zone name only
                    if (!string.IsNullOrEmpty(zone))
                    {
                        Log.Write($"[FocusField] Empty: {zone}");
                        SpeakField(zone, queued);
                    }
                    DuelState.CardDetailLines = null;
                    DuelState.CardDetailIndex = 0;
                    _lastUniqueId = 0;
                    return;
                }

                // Don't reveal opponent's face-down cards (when mrk is known
                // but card is still physically face-down on the field)
                if (!DuelState.IsMyPlayer(player))
                {
                    try
                    {
                        if (!Engine.GetCardFace(player, position, viewIndex))
                        {
                            string msg = !string.IsNullOrEmpty(zone)
                                ? $"Face-down card, {zone}"
                                : "Face-down card";
                            Log.Write($"[FocusField] {msg}");
                            SpeakField(msg, queued);
                            DuelState.CardDetailLines = null;
                            DuelState.CardDetailIndex = 0;
                            _lastUniqueId = uniqueId;
                            return;
                        }
                    }
                    catch (Exception ex) { Log.Write($"[FocusField] Face check: {ex.Message}"); }
                }

                // Dedup — suppress re-reading the same card instance
                if (uniqueId > 0 && uniqueId == _lastUniqueId) return;
                if (uniqueId > 0) _lastUniqueId = uniqueId;

                // Check if Link monster (no DEF, always Attack Mode)
                bool isLink = false;
                try
                {
                    var content = Content.s_instance;
                    if (content != null)
                        isLink = content.GetFrame(mrk) == Content.Frame.Link;
                }
                catch (Exception ex) { Log.Write($"[FocusField] Link check: {ex.Message}"); }

                // Battle mode and live stats for monster zones
                string battlePos = null;
                int? liveAtk = null, liveDef = null;
                if (IsMonsterZone(position))
                {
                    if (isLink)
                    {
                        battlePos = "Attack Mode";
                    }
                    else
                    {
                        try
                        {
                            bool face = Engine.GetCardFace(player, position, viewIndex);
                            bool turn = Engine.GetCardTurn(player, position, viewIndex);
                            battlePos = !face ? "Set" : turn ? "Defense Mode" : "Attack Mode";
                        }
                        catch (Exception ex) { Log.Write($"[FocusField] Battle mode check: {ex.Message}"); }
                    }
                }

                // Live stats via unique ID (works for all zones including Extra Monster)
                if (uniqueId > 0)
                {
                    try
                    {
                        var bv = Engine.GetBasicValByUniqueId(uniqueId);
                        liveAtk = bv.Atk;
                        if (!isLink) liveDef = bv.Def;
                    }
                    catch (Exception ex) { Log.Write($"[FocusField] BasicVal: {ex.Message}"); }
                }

                // Read card, override live stats, build detail lines for Ctrl+Up/Down
                var card = CardReader.ReadCardFromData(mrk);
                if (liveAtk.HasValue && !string.IsNullOrEmpty(card.Atk))
                    card.Atk = liveAtk.Value >= 0 ? liveAtk.Value.ToString() : "?";
                if (liveDef.HasValue && !string.IsNullOrEmpty(card.Def))
                    card.Def = liveDef.Value >= 0 ? liveDef.Value.ToString() : "?";

                var lines = card.GetDetailLines(out string summary, battlePosition: battlePos, zone: zone);
                DuelState.CardDetailLines = lines;
                DuelState.CardDetailIndex = 0;

                // Speak only the summary (name + position + zone)
                if (!string.IsNullOrEmpty(summary))
                    SpeakField(summary, queued);
            }
            catch (Exception ex) { Log.Write($"[PatchInvokeFocusField] {ex.Message}"); }
        }

        public static void ResetDedup() => _lastUniqueId = 0;

        /// <summary>
        /// Clear last focus tracking without re-queuing speech.
        /// Used when entering a selection list — the field context is replaced.
        /// </summary>
        public static void ClearLastFocus() => _hasLastFocus = false;

        /// <summary>
        /// Speak a deferred field focus queued after a selection title.
        /// Called from HandleTitle after the title is spoken.
        /// </summary>
        public static void SpeakDeferredFocus(int player, int position, int viewIndex)
        {
            // Skip pile zones — they're navigation artifacts, not selection targets
            if (position == Engine.PosExtra || position == Engine.PosDeck) return;

            string zone = GetZoneName(player, position, viewIndex);
            if (string.IsNullOrEmpty(zone)) return;

            int mrk = 0;
            try { mrk = Engine.GetCardID(player, position, viewIndex); }
            catch (Exception ex) { Log.Write($"[FocusField] Deferred card ID: {ex.Message}"); }

            // Opponent's face-down cards: game hides card ID (mrk=0) but card exists
            if (mrk <= 0 && !DuelState.IsMyPlayer(player) && (IsMonsterZone(position) || IsSpellTrapZone(position)))
            {
                try
                {
                    int count = Engine.GetCardNum(player, position);
                    if (count > 0)
                    {
                        Speech.SayQueued($"Face-down card, {zone}");
                        return;
                    }
                }
                catch (Exception ex) { Log.Write($"[FocusField] Deferred face-down check: {ex.Message}"); }
            }

            if (mrk <= 0)
            {
                if (!string.IsNullOrEmpty(zone))
                    Speech.SayQueued(zone);
                return;
            }

            // Don't reveal opponent's face-down cards (mrk known but physically face-down)
            if (!DuelState.IsMyPlayer(player))
            {
                try
                {
                    if (!Engine.GetCardFace(player, position, viewIndex))
                    {
                        Speech.SayQueued($"Face-down card, {zone}");
                        return;
                    }
                }
                catch (Exception ex) { Log.Write($"[FocusField] Deferred face check: {ex.Message}"); }
            }

            CardReader.SpeakCardFromData(mrk, zone, queued: true);
        }

        /// <summary>
        /// Re-queue the last focused field item. Called by dialog handlers
        /// when a dialog interrupts the auto-focused item.
        /// </summary>
        public static void RequeueLastFocus()
        {
            if (!_hasLastFocus) return;
            _hasLastFocus = false;
            SpeakDeferredFocus(_lastPlayer, _lastPosition, _lastViewIndex);
        }

        private static void SpeakField(string text, bool queued)
        {
            if (queued)
                Speech.SayQueued(text);
            else
                Speech.SayItem(text);
        }

        private static bool IsSpellTrapZone(int position)
        {
            return position == Engine.PosMagicLL || position == Engine.PosMagicL ||
                   position == Engine.PosMagicC || position == Engine.PosMagicR ||
                   position == Engine.PosMagicRR;
        }

        private static string GetZoneName(int player, int position, int viewIndex)
        {
            string side = !DuelState.IsMyPlayer(player) ? "Opponent's " : "";

            if (position == Engine.PosMonsterLL) return $"{side}Monster Zone 1";
            if (position == Engine.PosMonsterL) return $"{side}Monster Zone 2";
            if (position == Engine.PosMonsterC) return $"{side}Monster Zone 3";
            if (position == Engine.PosMonsterR) return $"{side}Monster Zone 4";
            if (position == Engine.PosMonsterRR) return $"{side}Monster Zone 5";
            if (position == Engine.PosMagicLL) return $"{side}Spell Trap Zone 1";
            if (position == Engine.PosMagicL) return $"{side}Spell Trap Zone 2";
            if (position == Engine.PosMagicC) return $"{side}Spell Trap Zone 3";
            if (position == Engine.PosMagicR) return $"{side}Spell Trap Zone 4";
            if (position == Engine.PosMagicRR) return $"{side}Spell Trap Zone 5";
            if (position == Engine.PosField) return $"{side}Field Spell Zone";
            if (position == Engine.PosPendulumLeft) return $"{side}Left Pendulum Zone";
            if (position == Engine.PosPendulumRight) return $"{side}Right Pendulum Zone";
            if (position == Engine.PosExLMonster) return $"{side}Extra Monster Zone Left";
            if (position == Engine.PosExRMonster) return $"{side}Extra Monster Zone Right";
            if (position == Engine.PosHand)
            {
                try
                {
                    int count = Engine.GetCardNum(player, Engine.PosHand);
                    if (count > 0 && viewIndex >= 0)
                        return $"{side}Hand, {viewIndex + 1} of {count}";
                }
                catch (Exception ex) { Log.Write($"[FocusField] Hand count: {ex.Message}"); }
                return $"{side}Hand";
            }
            if (position == Engine.PosExtra) return $"{side}Extra Deck";
            if (position == Engine.PosDeck) return $"{side}Deck";
            if (position == Engine.PosGrave)
            {
                try
                {
                    int count = Engine.GetCardNum(player, Engine.PosGrave);
                    if (count > 0)
                        return $"{side}Graveyard, {count} cards";
                }
                catch (Exception ex) { Log.Write($"[FocusField] Graveyard count: {ex.Message}"); }
                return $"{side}Graveyard";
            }
            if (position == Engine.PosExclude)
            {
                try
                {
                    int count = Engine.GetCardNum(player, Engine.PosExclude);
                    if (count > 0)
                        return $"{side}Banished, {count} cards";
                }
                catch (Exception ex) { Log.Write($"[FocusField] Banished count: {ex.Message}"); }
                return $"{side}Banished";
            }

            Log.Write($"[FocusField] Unknown position: player={player}, pos={position}, mapped to nothing");
            return null;
        }

        private static bool IsMonsterZone(int position)
        {
            return position == Engine.PosMonsterLL || position == Engine.PosMonsterL ||
                   position == Engine.PosMonsterC || position == Engine.PosMonsterR ||
                   position == Engine.PosMonsterRR || position == Engine.PosExLMonster ||
                   position == Engine.PosExRMonster;
        }
    }

    /// <summary>
    /// Announces opponent attack declarations via the game's native delegate.
    /// Fires when any monster declares an attack target.
    /// </summary>
    static class AttackTargetHandler
    {
        private static DuelClient.onDecideAttackTargetDelegate _handler;

        public static void Subscribe(DuelClient client)
        {
            _handler = (Action<int, int, int, int, int, int>)OnAttackDeclared;
            client.add_onDecideAttackTargetHandler(_handler);
            Log.Write("[AttackTarget] Subscribed to onDecideAttackTargetHandler");
        }

        static void OnAttackDeclared(int attackerPlayer, int attackerPosition, int attackerIndex,
            int targetPlayer, int targetPosition, int targetIndex)
        {
            try
            {
                // Only announce opponent's attacks
                if (DuelState.IsMyPlayer(attackerPlayer)) return;

                var content = Content.s_instance;
                if (content == null) return;

                // Get attacker name
                int attackerMrk = 0;
                try { attackerMrk = Engine.GetCardID(attackerPlayer, attackerPosition, attackerIndex); }
                catch (Exception ex) { Log.Write($"[AttackTarget] Attacker ID: {ex.Message}"); }
                string attackerName = attackerMrk > 0 ? content.GetName(attackerMrk) : "Unknown monster";

                // Get target — direct attack if no card at target position
                int targetMrk = 0;
                try { targetMrk = Engine.GetCardID(targetPlayer, targetPosition, targetIndex); }
                catch (Exception ex) { Log.Write($"[AttackTarget] Target ID: {ex.Message}"); }

                string announcement;
                if (targetMrk > 0)
                {
                    string targetName = content.GetName(targetMrk);
                    announcement = $"Opponent attacks {targetName} with {attackerName}";
                }
                else
                {
                    announcement = $"Opponent attacks directly with {attackerName}";
                }

                Log.Write($"[AttackTarget] {announcement}");
                Speech.SayQueued(announcement);
            }
            catch (Exception ex) { Log.Write($"[AttackTarget] {ex.Message}"); }
        }
    }

}
