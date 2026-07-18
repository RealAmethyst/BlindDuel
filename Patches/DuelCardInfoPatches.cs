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
    /// Card info panel reading — fires when any card gets focused in the duel.
    /// During duels:
    ///   - Hand cards: read directly from Engine database using CardInfoData.cardid
    ///     and speak immediately. No delayed UI read needed.
    ///   - Selection lists: use delayed ReadCardDelayed flow.
    ///   - Field cards: ignored here (handled by onFocusFieldHandler).
    /// Outside duels: fires normally for deck editor, card browser, etc.
    /// </summary>
    [HarmonyPatch(typeof(CardInfo), nameof(CardInfo.SetDescriptionArea))]
    class PatchCardInfoSetDescription
    {
        private const float ReadDelay = 0.15f;
        private static int _lastUniqueId;
        private static int _lastHandUniqueId;
        private static int _pendingMrk;
        private static int _pendingUniqueId;

        [HarmonyPostfix]
        static void Postfix(CardInfo __instance)
        {
            try
            {
                if (!__instance.gameObject.activeInHierarchy) return;

                if (NavigationState.IsInDuel)
                {
                    if (!DuelState.HasPhaseStarted) return;
                    if (DuelState.IsShowingResult) return;

                    try
                    {
                        var data = __instance.m_CardInfoData;

                        // Hand cards: read from Engine and speak immediately.
                        // Uses CardInfoData (position, cardid, index) as the trigger —
                        // the actual card data comes from the Engine database.
                        if (data.position == Engine.PosHand && DuelState.IsMyPlayer(data.player))
                        {
                            int mrk = data.cardid;
                            if (mrk <= 0) return;

                            int uid = data.uniqueid;
                            if (uid > 0 && uid == _lastHandUniqueId) return;
                            if (uid > 0) _lastHandUniqueId = uid;

                            int handCount = Engine.GetCardNum(data.player, Engine.PosHand);
                            int idx = data.index;
                            string zone = handCount > 0
                                ? $"Hand, {idx + 1} of {handCount}"
                                : "Hand";

                            Log.Write($"[HandCard] mrk={mrk}, uid={uid}, {zone}");
                            // Queue after game event messages (summon/activation)
                            // instead of interrupting them
                            bool queued = DuelState.MessageJustAnnounced;
                            DuelState.MessageJustAnnounced = false;
                            // Clear screen/dialog flags — hand navigation confirms
                            // the screen has been acknowledged (same as field focus)
                            NavigationState.ScreenJustAnnounced = false;
                            NavigationState.DialogJustAnnounced = false;

                            // Read card, build detail lines for Ctrl+Up/Down
                            BlindDuelCore.Preview.Clear();
                            var card = CardReader.ReadCardFromData(mrk);
                            var lines = card.GetDetailLines(out string summary, zone: zone);
                            DuelState.CardDetailLines = lines;
                            DuelState.CardDetailIndex = 0;

                            if (!string.IsNullOrEmpty(summary))
                            {
                                if (queued) Speech.SayQueued(summary);
                                else Speech.SayItem(summary);
                            }
                            return;
                        }

                        // Selection list cards are handled by DuelHandler.OnButtonFocused
                        // which reads ListCard.m_CardData directly. Nothing to do here.
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"[SetDescription] CardInfoData read failed: {ex.Message}");
                        return;
                    }
                }

                // Outside duels: read from UI panels (deck editor, card browser, etc.)
                try
                {
                    var data = __instance.m_CardInfoData;
                    _pendingMrk = data.cardid;
                    _pendingUniqueId = data.uniqueid;
                }
                catch (Exception ex)
                {
                    Log.Write($"[SetDescription] CardInfoData read failed: {ex.Message}");
                    _pendingMrk = 0;
                    _pendingUniqueId = 0;
                }

                BlindDuelCore.Instance.CancelInvoke(nameof(BlindDuelCore.ReadCardDelayed));
                BlindDuelCore.Instance.Invoke(nameof(BlindDuelCore.ReadCardDelayed), ReadDelay);
            }
            catch (Exception ex)
            {
                Log.Write($"[SetDescription] {ex.Message}");
            }
        }

        public static int PendingMrk => _pendingMrk;
        public static int PendingUniqueId => _pendingUniqueId;

        public static bool CheckAndUpdateDedup(int uniqueId)
        {
            if (uniqueId <= 0) return false;
            if (uniqueId == _lastUniqueId) return true;
            _lastUniqueId = uniqueId;
            return false;
        }

        public static void ResetDedup() => _lastUniqueId = 0;

        public static void ResetHandDedup() => _lastHandUniqueId = 0;
    }

}
