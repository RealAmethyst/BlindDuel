using System;
using UnityEngine;
using Il2CppYgomGame.Duel;

namespace BlindDuel
{
    /// <summary>
    /// Keyboard shortcuts for jumping to specific duel field zones.
    /// Sets the cursor location and calls FieldFocusHandler to announce
    /// the card at the target position.
    /// </summary>
    static class DuelFieldNav
    {
        public static void HandleInput(bool shift)
        {
            // LP reading (no cursor movement)
            if (Input.GetKeyDown(KeyCode.L))
            {
                int player = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                string who = shift ? "Opponent's" : "Your";
                int lp = DuelClient.GetLP(player);
                Speech.SayImmediate($"{who} life points: {lp}");
                return;
            }

            // All remaining keys move cursor and announce
            int? targetPlayer = null;
            int targetPos = 0;

            if (Input.GetKeyDown(KeyCode.C))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosHand;
            }
            else if (Input.GetKeyDown(KeyCode.M))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterLL;
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMagicLL;
            }
            else if (Input.GetKeyDown(KeyCode.T))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosField;
            }
            else if (Input.GetKeyDown(KeyCode.G))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosGrave;
            }
            else if (Input.GetKeyDown(KeyCode.B))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosExclude;
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosExtra;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterLL;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterL;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterC;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterR;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                targetPlayer = shift ? GetOpponent() : DuelState.GetMyPlayerNum();
                targetPos = Engine.PosMonsterRR;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                // Extra monster zones are shared (2 total, not per player)
                targetPlayer = DuelState.GetMyPlayerNum();
                targetPos = Engine.PosExLMonster;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                targetPlayer = DuelState.GetMyPlayerNum();
                targetPos = Engine.PosExRMonster;
            }

            if (targetPlayer == null) return;

            FieldFocusHandler.FocusPosition(targetPlayer.Value, targetPos);
        }

        static int GetOpponent()
        {
            return DuelState.GetMyPlayerNum() == 0 ? 1 : 0;
        }
    }
}
