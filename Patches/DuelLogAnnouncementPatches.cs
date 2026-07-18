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
    /// Shared helper for duel log patches. Reads ShowCardNameData entries added by
    /// AddRun*Log methods to announce opponent summons and card activations.
    /// </summary>
    static class DuelLogHelper
    {
        /// <summary>
        /// Captured in each summon/activation prefix so the postfix can find
        /// the newly added action (not the last action overall, which may be stale).
        /// </summary>
        private static int _prevActionCount;

        public static void CapturePrevActionCount(DuelLogController instance)
        {
            try { _prevActionCount = instance.m_DataList_ShowAction?.Count ?? 0; }
            catch { _prevActionCount = -1; }
        }

        public static void AnnounceSummon(DuelLogController instance, int prevCardNameCount, string defaultLabel)
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                if (prevCardNameCount < 0) return;

                // Find the newly added action (not the last one overall —
                // the list accumulates across the entire duel).
                string summonName = defaultLabel;
                int? actionOwner = null;
                try
                {
                    var actionList = instance.m_DataList_ShowAction;
                    if (actionList != null && _prevActionCount >= 0
                        && actionList.Count > _prevActionCount)
                    {
                        var newAction = actionList[_prevActionCount];
                        var datac = newAction.datac;
                        var actType = datac.acttype;
                        string specific = GetSummonTypeName(actType);
                        if (specific != null)
                            summonName = specific;
                        // Use datal.owner (actual player number) instead of datac.team
                        // (which reflects turn player, not card owner)
                        try { actionOwner = newAction.datal.owner; }
                        catch { }
                    }
                }
                catch (Exception ex) { Log.Write($"[DuelLogHelper] Summon owner lookup: {ex.Message}"); } // Property call on struct may fail — keep defaultLabel

                // Find new ShowCardNameData entries added by this method
                var cardList = instance.m_DataList_ShowCardName;
                if (cardList == null || cardList.Count <= prevCardNameCount) return;

                for (int i = prevCardNameCount; i < cardList.Count; i++)
                {
                    var card = cardList[i];
                    if (card.isCost) continue;

                    int cardId = card.cardid;
                    // Use datal.owner (player number) for accurate ownership
                    bool isOpponent = actionOwner.HasValue
                        ? !DuelState.IsMyPlayer(actionOwner.Value)
                        : DuelState.IsOpponentTeam(card.team);
                    string cardName = ResolveCardName(cardId);

                    Log.Write($"[DuelLog] {summonName}: {cardName} (owner={actionOwner}, cardTeam={card.team}, opponent={isOpponent}, cardid={cardId})");

                    DuelState.MessageJustAnnounced = true;
                    string prefix = isOpponent ? "Opponent " : "";
                    Speech.SayImmediate($"{prefix}{summonName}: {cardName}");
                    return; // Only announce the first non-cost card
                }
            }
            catch (Exception ex) { Log.Write($"[DuelLogHelper] {ex.Message}"); }
        }

        public static void AnnounceActivation(DuelLogController instance, int prevCardNameCount)
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                if (prevCardNameCount < 0) return;

                // Get action owner from the newly added action
                int? actionOwner = null;
                try
                {
                    var actionList = instance.m_DataList_ShowAction;
                    if (actionList != null && _prevActionCount >= 0
                        && actionList.Count > _prevActionCount)
                    {
                        try { actionOwner = actionList[_prevActionCount].datal.owner; }
                        catch { }
                    }
                }
                catch (Exception ex) { Log.Write($"[DuelLogHelper] Activation owner lookup: {ex.Message}"); }

                var cardList = instance.m_DataList_ShowCardName;
                if (cardList == null || cardList.Count <= prevCardNameCount) return;

                for (int i = prevCardNameCount; i < cardList.Count; i++)
                {
                    var card = cardList[i];
                    if (card.isCost) continue;

                    int cardId = card.cardid;
                    bool isOpponent = actionOwner.HasValue
                        ? !DuelState.IsMyPlayer(actionOwner.Value)
                        : DuelState.IsOpponentTeam(card.team);
                    string cardName = ResolveCardName(cardId);

                    Log.Write($"[DuelLog] Activates: {cardName} (owner={actionOwner}, cardTeam={card.team}, opponent={isOpponent}, cardid={cardId})");

                    DuelState.MessageJustAnnounced = true;
                    string prefix = isOpponent ? "Opponent activates" : "Activate";
                    Speech.SayImmediate($"{prefix}: {cardName}");
                    return;
                }
            }
            catch (Exception ex) { Log.Write($"[DuelLogHelper] Activation: {ex.Message}"); }
        }

        public static string ResolveCardName(int cardId)
        {
            try
            {
                var content = Content.s_instance;
                if (content != null && cardId > 0)
                    return content.GetName(cardId);
            }
            catch (Exception ex) { Log.Write($"[DuelLogHelper] ResolveCardName: {ex.Message}"); }
            return "unknown";
        }

        /// <summary>
        /// Returns a specific summon type name, or null if the type is unrecognized
        /// (so the caller keeps its default label).
        /// </summary>
        static string GetSummonTypeName(LOGACTIONTYPE type) => type switch
        {
            LOGACTIONTYPE.ACTION_SUMMON => "Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON => "Special Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_RITUAL => "Ritual Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_FUSION => "Fusion Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_SYNC => "Synchro Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_XYZ => "Xyz Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_PENDULUM => "Pendulum Summon",
            LOGACTIONTYPE.ACTION_SPSUMMON_LINK => "Link Summon",
            LOGACTIONTYPE.ACTION_REVERSESUMMON => "Flip Summon",
            _ => null
        };
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.AddRunSummonLog))]
    class PatchAddRunSummonLog
    {
        [HarmonyPrefix]
        static void Prefix(DuelLogController __instance, ref int __state)
        {
            try { __state = __instance.m_DataList_ShowCardName?.Count ?? 0; }
            catch { __state = -1; }
            DuelLogHelper.CapturePrevActionCount(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(DuelLogController __instance, int __state)
        {
            DuelLogHelper.AnnounceSummon(__instance, __state, "Summon");
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.AddRunSpSummonLog))]
    class PatchAddRunSpSummonLog
    {
        [HarmonyPrefix]
        static void Prefix(DuelLogController __instance, ref int __state)
        {
            try { __state = __instance.m_DataList_ShowCardName?.Count ?? 0; }
            catch { __state = -1; }
            DuelLogHelper.CapturePrevActionCount(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(DuelLogController __instance, int __state)
        {
            DuelLogHelper.AnnounceSummon(__instance, __state, "Special Summon");
        }
    }

    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.AddRunFusionLog))]
    class PatchAddRunFusionLog
    {
        [HarmonyPrefix]
        static void Prefix(DuelLogController __instance, ref int __state)
        {
            try { __state = __instance.m_DataList_ShowCardName?.Count ?? 0; }
            catch { __state = -1; }
            DuelLogHelper.CapturePrevActionCount(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(DuelLogController __instance, int __state)
        {
            DuelLogHelper.AnnounceSummon(__instance, __state, "Fusion Summon");
        }
    }

    /// <summary>
    /// Announces opponent card activations (effect activation, hand traps, etc.).
    /// When the opponent activates a card, the player hears the card name
    /// before/alongside the effect text from InstantMessage.
    /// </summary>
    [HarmonyPatch(typeof(DuelLogController), nameof(DuelLogController.AddCardHappenLog))]
    class PatchAddCardHappenLog
    {
        [HarmonyPrefix]
        static void Prefix(DuelLogController __instance, ref int __state)
        {
            try { __state = __instance.m_DataList_ShowCardName?.Count ?? 0; }
            catch { __state = -1; }
            DuelLogHelper.CapturePrevActionCount(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(DuelLogController __instance, int __state)
        {
            DuelLogHelper.AnnounceActivation(__instance, __state);
        }
    }

}
