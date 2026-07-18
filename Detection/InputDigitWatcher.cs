using System;
using Il2CppYgomGame.Menu.Common;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    // InputDigit dialogs live under UI/OverlayCanvas/DialogManager rather than the
    // main ContentManager that ScreenDetector.GetFocusVC inspects, so HandlerRegistry
    // doesn't route to them. We find the active instance via Resources lookup and
    // poll _currentValue so +/- clicks become audible.
    public static class InputDigitWatcher
    {
        private static int _lastValue = int.MinValue;

        // The dialog's "-" button label reads as a sign that glues to the trailing
        // sibling-index ("- 2 of 2" → "minus 2"). Substitute verbal labels for the
        // four step buttons so the screen reader pronounces them unambiguously.
        public static string GetButtonLabel(SelectionButton button) => button?.name switch
        {
            "LargeMinusButton" => "minus 10",
            "MinusButton" => "minus 1",
            "PlusButton" => "plus 1",
            "LargePlusButton" => "plus 10",
            _ => null,
        };

        public static void Poll()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<InputDigitViewController>();
                InputDigitViewController vc = null;
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var candidate = all[i];
                        if (candidate != null && candidate.gameObject.activeInHierarchy)
                        {
                            vc = candidate;
                            break;
                        }
                    }
                }

                if (vc == null)
                {
                    _lastValue = int.MinValue;
                    return;
                }

                int current = vc._currentValue;
                if (_lastValue == int.MinValue)
                {
                    _lastValue = current;
                    return;
                }
                if (current != _lastValue)
                {
                    _lastValue = current;
                    Speech.SayImmediate(current.ToString());
                }
            }
            catch (Exception ex) { Log.Write($"[InputDigitWatcher] {ex.Message}"); }
        }
    }
}
