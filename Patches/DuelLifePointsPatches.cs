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
    [HarmonyPatch(typeof(DuelLP), nameof(DuelLP.SetLP))]
    class PatchSetLP
    {
        [HarmonyPostfix]
        static void Postfix(DuelLP __instance, int lp, bool initialSet)
        {
            try
            {
                if (!initialSet) return;
                string who = __instance.m_IsNear ? "Your" : "Opponent's";
                Speech.SayQueued($"{who} starting life points: {lp}");
            }
            catch (Exception ex) { Log.Write($"[PatchSetLP] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(DuelLP), nameof(DuelLP.ChangeLP), MethodType.Normal)]
    class PatchChangeLP
    {
        [HarmonyPostfix]
        static void Postfix(DuelLP __instance, int afterLP, int damage, Engine.DamageType type)
        {
            try
            {
                string who = __instance.m_IsNear ? "Your" : "Opponent's";

                string reason = type switch
                {
                    Engine.DamageType.ByBattle => "battle damage",
                    Engine.DamageType.ByEffect => "effect damage",
                    Engine.DamageType.ByCost => "cost",
                    Engine.DamageType.ByPay => "payment",
                    Engine.DamageType.ByLost => "lost",
                    Engine.DamageType.Recover => "recovery",
                    _ => ""
                };

                if (type == Engine.DamageType.Recover)
                    Speech.SayImmediate($"{who} life points: {afterLP}, gained {damage} from {reason}");
                else if (damage > 0 && reason.Length > 0)
                    Speech.SayImmediate($"{who} life points: {afterLP}, took {damage} {reason}");
                else
                    Speech.SayImmediate($"{who} life points: {afterLP}");

                if (afterLP < 1)
                {
                    NavigationState.IsInDuel = false;
                    DuelState.Clear();
                }
            }
            catch (Exception ex) { Log.Write($"[PatchChangeLP] {ex.Message}"); }
        }
    }

}
