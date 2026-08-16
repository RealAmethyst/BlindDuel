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
    [HarmonyPatch(typeof(DuelEndOperation), nameof(DuelEndOperation.Setup))]
    class PatchDuelEndOperationSetup
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                // Fires early when duel end sequence begins (before animation).
                // Suppress button speech until DuelEndMessage.Setup populates the result text.
                DuelState.IsShowingResult = true;
                Log.Write("[DuelEndOp] Duel end sequence started, suppressing buttons");
            }
            catch (Exception ex) { Log.Write($"[PatchDuelEndOperationSetup] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(DuelEndMessage), nameof(DuelEndMessage.Setup))]
    class PatchDuelEndMessageSetup
    {
        [HarmonyPostfix]
        static void Postfix(string message, bool winMyself, bool winRival)
        {
            try
            {
                string result = winMyself ? "Victory" : winRival ? "Defeat" : "Draw";

                string cleaned = !string.IsNullOrWhiteSpace(message)
                    ? TextUtil.StripTags(message)
                    : "";

                string announcement = !string.IsNullOrEmpty(cleaned)
                    ? $"{result}. {cleaned}"
                    : result;

                Log.Write($"[DuelEnd] {announcement}");
                Speech.SayImmediate(announcement);

                // Result has been spoken — allow buttons and mark duel as over
                NavigationState.IsInDuel = false;
                DuelState.Clear();
            }
            catch (Exception ex) { Log.Write($"[PatchDuelEndMessageSetup] {ex.Message}"); }
        }
    }

    /// <summary>
    /// Read match tips pages (MDMarkup content shown at the start of solo practice duels).
    /// Only speaks during duels to avoid interfering with menu MDMarkup reading.
    /// </summary>
    [HarmonyPatch(typeof(MDMarkupPageWidgetBase), nameof(MDMarkupPageWidgetBase.BindContentData))]
    class PatchMDMarkupBindContent
    {
        [HarmonyPostfix]
        static void Postfix(MDMarkupPageWidgetBase __instance)
        {
            try
            {
                if (!NavigationState.IsInDuel) return;

                string title = __instance.m_CaptionText?.text;
                string body = __instance.m_Text?.text;

                title = TextUtil.StripTags(title);
                body = TextUtil.StripTags(body);

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return;

                string announcement = !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body)
                    ? $"{title}. {body}"
                    : title ?? body;

                Log.Write($"[MatchTips] {announcement}");
                Speech.SayQueued(announcement);
            }
            catch (Exception ex) { Log.Write($"[PatchMDMarkupBindContent] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CardCommand), nameof(CardCommand.Open), new Type[0])]
    class PatchCardCommandOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                Log.Write("[CardCommand] Action menu opened — silencing card speech");
                Speech.Silence();
            }
            catch (Exception ex) { Log.Write($"[PatchCardCommandOpen] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CardCommand), nameof(CardCommand.Close))]
    class PatchCardCommandClose
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                // Suppress the next field focus — a selection prompt message
                // will follow shortly and should speak first.
                DuelState.SuppressNextFieldFocus = true;
            }
            catch (Exception ex) { Log.Write($"[PatchCardCommandClose] {ex.Message}"); }
        }
    }

    /// <summary>
    /// When battle position selection opens (Attack/Defense choice during summon),
    /// queue the first auto-focused button so it doesn't interrupt any preceding speech.
    /// SetDefaultPosition is called by the game when initializing position buttons.
    /// </summary>
    [HarmonyPatch(typeof(CardCommandEx), nameof(CardCommandEx.SetDefaultPosition))]
    class PatchPositionSelectOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                if (!NavigationState.IsInDuel) return;
                Log.Write("[PositionSelect] Battle position selection opened");
                NavigationState.DialogJustAnnounced = true;
                DuelState.HasPendingSelection = true;
                DuelState.DeferredSelectionButton = null;
                DuelState.DeferredFieldFocus = null;
            }
            catch (Exception ex) { Log.Write($"[PatchPositionSelectOpen] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(EffectTaskPhaseChange), nameof(EffectTaskPhaseChange.PlayPhaseChangeEffect))]
    class PatchPhaseChange
    {
        private static Engine.Phase _lastPhase = Engine.Phase.Null;

        [HarmonyPostfix]
        static void Postfix(EffectTaskPhaseChange __instance)
        {
            try
            {
                DuelState.HasPhaseStarted = true;

                var phase = __instance.phase;
                if (phase == _lastPhase) return;
                _lastPhase = phase;

                string phaseName = phase switch
                {
                    Engine.Phase.Draw => "Draw Phase",
                    Engine.Phase.Standby => "Standby Phase",
                    Engine.Phase.Main1 => "Main Phase 1",
                    Engine.Phase.Battle => "Battle Phase",
                    Engine.Phase.Main2 => "Main Phase 2",
                    Engine.Phase.End => "End Phase",
                    _ => ""
                };

                if (string.IsNullOrEmpty(phaseName)) return;

                Log.Write($"[PhaseChange] {phaseName}");
                Speech.SayQueued(phaseName);
            }
            catch (Exception ex) { Log.Write($"[PatchPhaseChange] {ex.Message}"); }
        }
    }

}
