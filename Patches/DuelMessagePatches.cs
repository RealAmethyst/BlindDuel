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
    // --- Tutorial & Instant Message patches ---

    [HarmonyPatch(typeof(TutorialNavigator), nameof(TutorialNavigator.PlayCenterMsg),
        new Type[] { typeof(Il2CppSystem.Collections.Generic.IList<string>), typeof(UnityEngine.Events.UnityAction), typeof(float) })]
    class PatchTutorialCenterMsg
    {
        private static string _lastMessage = "";

        [HarmonyPostfix]
        static void Postfix(Il2CppSystem.Collections.Generic.IList<string> messages)
        {
            try
            {
                if (messages == null) return;

                var parts = new System.Collections.Generic.List<string>();
                // Il2Cpp IList doesn't expose Count directly — iterate by index with bounds check
                for (int i = 0; ; i++)
                {
                    string msg;
                    try { msg = messages[i]; }
                    catch { break; }
                    if (!string.IsNullOrWhiteSpace(msg))
                        parts.Add(TextUtil.StripTags(msg));
                }

                string combined = string.Join(". ", parts);
                if (string.IsNullOrWhiteSpace(combined) || combined == _lastMessage) return;

                _lastMessage = combined;
                Log.Write($"[TutorialCenter] {combined}");
                if (NavigationState.IsInDuel)
                {
                    DuelState.MessageJustAnnounced = true;
                    Speech.SayImmediate(combined);
                }
                else
                {
                    Speech.SayQueued(combined);
                }
            }
            catch (Exception ex) { Log.Write($"[PatchTutorialCenterMsg] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(TutorialNavigator), nameof(TutorialNavigator.PlayTopMsg),
        new Type[] { typeof(string), typeof(float) })]
    class PatchTutorialTopMsg
    {
        private static string _lastMessage = "";

        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned) || cleaned == _lastMessage) return;

                _lastMessage = cleaned;
                Log.Write($"[TutorialTop] {cleaned}");
                if (NavigationState.IsInDuel)
                {
                    DuelState.MessageJustAnnounced = true;
                    Speech.SayImmediate(cleaned);
                }
                else
                {
                    Speech.SayQueued(cleaned);
                }
            }
            catch (Exception ex) { Log.Write($"[PatchTutorialTopMsg] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InstantMessage), nameof(InstantMessage.Open))]
    class PatchInstantMessageOpen
    {
        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;
                if (cleaned == InstantMessageDedup.LastMessage) return;
                InstantMessageDedup.LastMessage = cleaned;

                Log.Write($"[InstantMessage] {cleaned}");
                if (NavigationState.IsInDuel)
                {
                    // If a summon/activation just announced, queue after it
                    // instead of interrupting (effect text arrives ~8ms later)
                    if (DuelState.MessageJustAnnounced)
                        Speech.SayQueued(cleaned);
                    else
                    {
                        DuelState.MessageJustAnnounced = true;
                        Speech.SayImmediate(cleaned);
                    }
                }
                else
                {
                    Speech.SayQueued(cleaned);
                }
            }
            catch (Exception ex) { Log.Write($"[PatchInstantMessageOpen] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InstantMessage), nameof(InstantMessage.ReqOpen))]
    class PatchInstantMessageReqOpen
    {
        [HarmonyPostfix]
        static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                string cleaned = TextUtil.StripTags(message);
                if (string.IsNullOrWhiteSpace(cleaned)) return;
                if (cleaned == InstantMessageDedup.LastMessage) return;
                InstantMessageDedup.LastMessage = cleaned;

                Log.Write($"[InstantMessageReq] {cleaned}");
                if (NavigationState.IsInDuel)
                {
                    if (DuelState.MessageJustAnnounced)
                        Speech.SayQueued(cleaned);
                    else
                    {
                        DuelState.MessageJustAnnounced = true;
                        Speech.SayImmediate(cleaned);
                    }
                }
                else
                {
                    Speech.SayQueued(cleaned);
                }
            }
            catch (Exception ex) { Log.Write($"[PatchInstantMessageReqOpen] {ex.Message}"); }
        }
    }

    /// <summary>
    /// Shared dedup between InstantMessage.Open and InstantMessage.ReqOpen
    /// to prevent the same message speaking twice (ReqOpen queues, Open displays).
    /// </summary>
    static class InstantMessageDedup
    {
        public static string LastMessage = "";
    }

}
