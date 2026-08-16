using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppYgomSystem;
using Il2CppYgomSystem.UI;
using HarmonyLib;

namespace BlindDuel
{
    /// <summary>
    /// Injects keyboard input into the game's gamepad system.
    /// Arrow keys → d-pad, Enter → confirm, Backspace → cancel.
    /// Ctrl+Up/Down is suppressed during duels (handled by BlindDuelCore instead).
    /// </summary>
    [HarmonyPatch(typeof(GamePad_PC), nameof(GamePad_PC.GetKeyDown))]
    class PatchGamePadGetKeyDown
    {
        private static Dictionary<int, KeyCode> _map;

        // Must run BEFORE BrowseDirectionPatches: this postfix mutates __result
        // (suppression + keyboard mapping) and BrowseDirection must observe the final value.
        // Harmony runs higher-priority postfixes first, so High beats BrowseDirection's Low.
        [HarmonyPriority(Priority.High)]
        static void Postfix(int Type, ref bool __result)
        {
            try
            {
                // Suppress right stick up/down from game during duels (SDL3 reads it directly)
                if (__result && InputMap.IsRightStickDuelSuppress(Type)) { __result = false; return; }

                if (__result) return;
                if (!Application.isFocused) return;

                var map = GetMap();
                if (map == null) return;

                if (!map.TryGetValue(Type, out var keyCode)) return;

                // Suppress Up/Down when Ctrl is held during a duel (mod handles those)
                if (IsCtrlDuelSuppress(Type)) return;
                // Escape → Option2 (touchpad) only during duels
                if (InputMap.IsDuelOnlySuppress(Type)) return;
                // Don't fire Cancel (Backspace) while a text input field is active —
                // the user is deleting characters, not cancelling the screen.
                if (InputMap.IsInputEditingSuppress(Type)) return;

                __result = Input.GetKeyDown(keyCode);
            }
            catch (Exception ex)
            {
                Log.Write($"[PatchGamePadGetKeyDown] {ex.Message}");
            }
        }

        static Dictionary<int, KeyCode> GetMap() => _map ??= InputMap.Build();
        static bool IsCtrlDuelSuppress(int type) => InputMap.IsCtrlDuelSuppress(type);
    }

    [HarmonyPatch(typeof(GamePad_PC), nameof(GamePad_PC.GetKey))]
    class PatchGamePadGetKey
    {
        private static Dictionary<int, KeyCode> _map;

        // Must run BEFORE BrowseDirectionPatches: this postfix mutates __result
        // (suppression + keyboard mapping) and BrowseDirection must observe the final value.
        // Harmony runs higher-priority postfixes first, so High beats BrowseDirection's Low.
        [HarmonyPriority(Priority.High)]
        static void Postfix(int Type, ref bool __result)
        {
            try
            {
                if (__result && InputMap.IsRightStickDuelSuppress(Type)) { __result = false; return; }

                if (__result) return;
                if (!Application.isFocused) return;

                var map = GetMap();
                if (map == null) return;

                if (!map.TryGetValue(Type, out var keyCode)) return;

                if (IsCtrlDuelSuppress(Type)) return;
                if (InputMap.IsDuelOnlySuppress(Type)) return;
                if (InputMap.IsInputEditingSuppress(Type)) return;

                __result = Input.GetKey(keyCode);
            }
            catch (Exception ex)
            {
                Log.Write($"[PatchGamePadGetKey] {ex.Message}");
            }
        }

        static Dictionary<int, KeyCode> GetMap() => _map ??= InputMap.Build();
        static bool IsCtrlDuelSuppress(int type) => InputMap.IsCtrlDuelSuppress(type);
    }

    /// <summary>
    /// Shared mapping logic for keyboard → gamepad button injection.
    /// </summary>
    static class InputMap
    {
        private static int _btnUp, _btnDown, _btnLeft, _btnRight, _btnDecision, _btnCancel;
        private static int _btnRUp, _btnRDown;
        private static int _btnSub1, _btnSub2;
        private static int _btnL2, _btnR2;
        private static int _btnOption1;
        private static int _btnOption2;
        private static int _btnL1, _btnR1;
        private static int _btnL3;


        public static Dictionary<int, KeyCode> Build()
        {
            try
            {
                _btnUp = GamePad.BUTTON_UP;
                _btnDown = GamePad.BUTTON_DOWN;
                _btnLeft = GamePad.BUTTON_LEFT;
                _btnRight = GamePad.BUTTON_RIGHT;
                _btnDecision = GamePad.BUTTON_FUNC_DECISION;
                _btnCancel = GamePad.BUTTON_FUNC_CANCEL;
                _btnRUp = GamePad.BUTTON_RUP;
                _btnRDown = GamePad.BUTTON_RDOWN;
                _btnSub1 = SelectorManager.GetGamePadKeyConfig(SelectorManager.KeyType.Sub1);
                _btnSub2 = SelectorManager.GetGamePadKeyConfig(SelectorManager.KeyType.Sub2);
                _btnL2 = GamePad.BUTTON_L2;
                _btnR2 = GamePad.BUTTON_R2;
                _btnOption1 = SelectorManager.GetGamePadKeyConfig(SelectorManager.KeyType.Option1);
                _btnOption2 = SelectorManager.GetGamePadKeyConfig(SelectorManager.KeyType.Option2);
                _btnL1 = GamePad.BUTTON_L1;
                _btnR1 = GamePad.BUTTON_R1;
                _btnL3 = GamePad.BUTTON_L3;

                return new Dictionary<int, KeyCode>
                {
                    { _btnUp, KeyCode.UpArrow },
                    { _btnDown, KeyCode.DownArrow },
                    { _btnLeft, KeyCode.LeftArrow },
                    { _btnRight, KeyCode.RightArrow },
                    { _btnDecision, KeyCode.Return },
                    { _btnCancel, KeyCode.Backspace },
                    { _btnSub2, KeyCode.E },      // Y button
                    { _btnSub1, KeyCode.F },       // X button
                    { _btnL2, KeyCode.Z },         // Left trigger
                    { _btnR2, KeyCode.X },         // Right trigger
                    { _btnOption1, KeyCode.Tab },  // Start/Menu button
                    { _btnOption2, KeyCode.Escape }, // Touchpad (duel settings/concede)
                    { _btnL1, KeyCode.I },         // Left bumper
                    { _btnR1, KeyCode.O },         // Right bumper
                    { _btnL3, KeyCode.D },         // Left stick click
                };
            }
            catch (Exception ex)
            {
                Log.Write($"[InputMap] Failed to read GamePad button constants: {ex.Message}");
                return null;
            }
        }

        public static bool IsCtrlDuelSuppress(int type)
        {
            if (!NavigationState.IsInDuel) return false;
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;
            return type == _btnUp || type == _btnDown;
        }

        /// <summary>
        /// Suppress right stick up/down from game navigation during duels
        /// (used for card detail line-by-line reading instead).
        /// </summary>
        public static bool IsRightStickDuelSuppress(int type)
        {
            if (!NavigationState.IsInDuel) return false;
            return type == _btnRUp || type == _btnRDown;
        }

        /// <summary>
        /// Suppress Escape → Option2 (touchpad) outside of duels.
        /// Escape is already used by the game for other screens.
        /// Suppress D → L3 (left stick click) during duels.
        /// </summary>
        public static bool IsDuelOnlySuppress(int type)
        {
            if (type == _btnOption2) return !NavigationState.IsInDuel;
            if (type == _btnL3) return NavigationState.IsInDuel;
            return false;
        }

        /// <summary>
        /// Suppress Backspace → Cancel while an input field is being edited
        /// (backspace should delete characters, not close the screen).
        /// </summary>
        public static bool IsInputEditingSuppress(int type)
        {
            if (type != _btnCancel) return false;
            return PatchInputFieldValueChanged.IsEditing;
        }
    }
}
