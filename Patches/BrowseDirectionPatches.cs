using System;
using HarmonyLib;
using Il2CppYgomSystem;
using UnityEngine;

namespace BlindDuel
{
    [HarmonyPatch(typeof(GamePad_PC), nameof(GamePad_PC.GetKeyDown))]
    class PatchBrowseDirectionGetKeyDown
    {
        static void Postfix(int Type, ref bool __result)
        {
            BrowseDirectionTracker.TrackKeyDown(Type, __result);
        }
    }

    [HarmonyPatch(typeof(GamePad_PC), nameof(GamePad_PC.GetKey))]
    class PatchBrowseDirectionGetKey
    {
        static void Postfix(int Type, ref bool __result)
        {
            BrowseDirectionTracker.TrackKey(Type, __result);
        }
    }

    static class BrowseDirectionTracker
    {
        public static void TrackKeyDown(int type, bool result) => Track(type, result, Input.GetKeyDown);

        public static void TrackKey(int type, bool result) => Track(type, result, Input.GetKey);

        private static void Track(int type, bool result, Func<KeyCode, bool> isKeyActive)
        {
            if (DuelState.LastBrowsePosition < 0) return;
            if (!Application.isFocused) return;

            if (type == GamePad.BUTTON_DOWN && (result || isKeyActive(KeyCode.DownArrow)))
            {
                DuelState.BrowseDirection = 1;
                return;
            }

            if (type == GamePad.BUTTON_UP && (result || isKeyActive(KeyCode.UpArrow)))
            {
                DuelState.BrowseDirection = -1;
            }
        }
    }
}
