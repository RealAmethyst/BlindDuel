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
    [HarmonyPatch(typeof(DuelInfoDialogBase), nameof(DuelInfoDialogBase.Open))]
    class PatchDuelInfoDialogOpen
    {
        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;

                Log.Write($"[DuelInfoDialog] {cleaned}");
                if (NavigationState.IsInDuel)
                {
                    DuelState.MessageJustAnnounced = true;
                    Speech.SayImmediate(cleaned);

                    // Re-queue the field item that was just interrupted.
                    // The field focus fires ~4ms before this dialog in the same frame,
                    // so the item spoke first and got cut off by SayImmediate above.
                    FieldFocusHandler.RequeueLastFocus();
                }
                else
                {
                    Speech.SayQueued(cleaned);
                }
            }
            catch (Exception ex) { Log.Write($"[PatchDuelInfoDialog] {ex.Message}"); }
        }
    }

    [HarmonyPatch]
    class PatchDuelConfirmDialogOpen
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();
            foreach (var m in typeof(DuelConfirmDialog).GetMethods())
            {
                if (m.Name == nameof(DuelConfirmDialog.Open))
                    methods.Add(m);
            }
            return methods;
        }

        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;

                Log.Write($"[DuelConfirmDialog] {cleaned}");
                Speech.SayImmediate(cleaned);
            }
            catch (Exception ex) { Log.Write($"[PatchDuelConfirmDialogOpen] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(DuelSelectDialog), nameof(DuelSelectDialog.Open))]
    class PatchDuelSelectDialogOpen
    {
        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                PatchDuelSelectDialogUpdateMessage.ResetDedup();

                if (string.IsNullOrWhiteSpace(message)) return;
                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;

                Log.Write($"[DuelSelectDialog] {cleaned}");
                Speech.SayImmediate(cleaned);
            }
            catch (Exception ex) { Log.Write($"[PatchDuelSelectDialogOpen] {ex.Message}"); }
        }
    }

    /// <summary>
    /// Announces the card/effect when navigating between tabs in a DuelSelectDialog
    /// (e.g., the "Activate a card or effect?" popup). The card tabs are SelectionItems,
    /// not SelectionButtons, so our OnSelected patch doesn't fire for them.
    /// UpdateMessage fires when the selected tab changes (both focus and confirm).
    /// </summary>
    [HarmonyPatch(typeof(DuelSelectDialog), nameof(DuelSelectDialog.UpdateMessage))]
    class PatchDuelSelectDialogUpdateMessage
    {
        private static int _lastIndex = -1;

        [HarmonyPostfix]
        static void Postfix(DuelSelectDialog __instance, int effectIndex)
        {
            try
            {
                if (effectIndex == _lastIndex) return;
                _lastIndex = effectIndex;

                var infoList = __instance.infoList;
                if (infoList == null || effectIndex < 0 || effectIndex >= infoList.Count) return;

                string message = infoList[effectIndex].message;
                if (string.IsNullOrWhiteSpace(message)) return;

                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;

                Log.Write($"[DuelSelectTab] Tab {effectIndex}: {cleaned}");
                Speech.SayItem(cleaned);
            }
            catch (Exception ex) { Log.Write($"[PatchDuelSelectDialogUpdateMessage] {ex.Message}"); }
        }

        public static void ResetDedup() => _lastIndex = -1;
    }

}
