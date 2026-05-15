using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppYgomGame.Menu;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    /// <summary>
    /// Exposes shortcut-only buttons (Sort, Filter, Clear, NotOwn, Search, Save,
    /// Menu, etc.) as an arrow-navigable virtual list. L1 (keyboard I) toggles;
    /// Left/Right cycles; Enter activates; Up/Down/Backspace/Escape exit.
    /// Handlers opt in via EnableFor; the L1 hijack lives in InputPatches.
    /// </summary>
    public static class VirtualToolbar
    {
        private static IMenuHandler _owner;
        private static bool _active;
        private static readonly List<Entry> _entries = new();
        private static int _index;

        // Dialogs that have their own toolbar-style action strip (OK/Cancel/Help, etc.)
        // and should keep the virtual toolbar available while they're open.
        private static readonly HashSet<string> ToolbarFriendlyDialogs = new() { "FilterDialogUI" };

        public static bool IsActive => _active;

        public static void EnableFor(IMenuHandler handler) => _owner = handler;

        public static void Disable()
        {
            _owner = null;
            if (_active) Exit(silent: true);
        }

        /// <summary>Closes the toolbar if active. Used by the OnBack/SaveDialog patches.</summary>
        public static bool OnBackPressed()
        {
            if (!_active) return false;
            Exit(silent: false);
            return true;
        }

        /// <summary>True when an owner handler is active and (no dialog OR a toolbar-friendly dialog).</summary>
        public static bool CanToggle()
        {
            if (_owner == null || HandlerRegistry.Current != _owner) return false;
            string dialog = NavigationState.LastDialogTitle;
            if (!string.IsNullOrEmpty(dialog) && !ToolbarFriendlyDialogs.Contains(dialog)) return false;
            return true;
        }

        public static void RequestOpen()  { if (!_active) Enter(); }
        public static void RequestClose() { if (_active) Exit(silent: false); }

        public static void Update()
        {
            if (_owner == null || HandlerRegistry.Current != _owner)
            {
                if (_active) Exit(silent: true);
                return;
            }

            // Most dialogs close the toolbar. Whitelisted ones (FilterDialog) keep it
            // available so the dialog's own action buttons can be navigated too.
            string dialog = NavigationState.LastDialogTitle;
            if (!string.IsNullOrEmpty(dialog) && !ToolbarFriendlyDialogs.Contains(dialog))
            {
                if (_active) Exit(silent: true);
                return;
            }

            // L1 toggles: keyboard I, or XInput L1 (joystick button 4).
            bool togglePressed = Input.GetKeyDown(KeyCode.I)
                              || Input.GetKeyDown(KeyCode.JoystickButton4);

            if (!_active)
            {
                if (togglePressed) Enter();
                return;
            }

            if (togglePressed) { Exit(silent: false); return; }

            if (Input.GetKeyDown(KeyCode.LeftArrow))            MovePrev();
            else if (Input.GetKeyDown(KeyCode.RightArrow))      MoveNext();
            else if (Input.GetKeyDown(KeyCode.Return))          Activate();
            // Backspace/Escape are handled by the OnBack/SaveDialog patches.
            else if (Input.GetKeyDown(KeyCode.UpArrow)
                  || Input.GetKeyDown(KeyCode.DownArrow))       Exit(silent: false);
        }

        private static void Enter()
        {
            BuildEntries();
            if (_entries.Count == 0)
            {
                Speech.SayImmediate("No toolbar shortcuts here");
                return;
            }
            _active = true;
            _index = 0;
            AnnounceCurrent("Toolbar. ");
        }

        private static void Exit(bool silent)
        {
            if (!_active) return;
            _active = false;
            _entries.Clear();
            if (!silent) Speech.SayImmediate("Toolbar closed");
        }

        private static void MoveNext()
        {
            if (_entries.Count == 0) return;
            _index = (_index + 1) % _entries.Count;
            AnnounceCurrent();
        }

        private static void MovePrev()
        {
            if (_entries.Count == 0) return;
            _index = (_index - 1 + _entries.Count) % _entries.Count;
            AnnounceCurrent();
        }

        private static void Activate()
        {
            if (_entries.Count == 0) return;
            var entry = _entries[_index];
            if (entry.Button == null)
            {
                Speech.SayImmediate("Button unavailable");
                return;
            }
            try
            {
                entry.Button.OnClick();
                bool? state = GetToggleState(entry.Button);
                if (state.HasValue)
                    Speech.SayImmediate($"{entry.Label}, {(state.Value ? "on" : "off")}");
                else
                    Speech.SayImmediate($"{entry.Label} activated");
                Exit(silent: true);
            }
            catch (Exception ex)
            {
                Log.Write($"[VirtualToolbar] OnClick failed for {entry.Label}: {ex.Message}");
                Speech.SayImmediate("Activation failed");
            }
        }

        private static void AnnounceCurrent(string prefix = "")
        {
            var entry = _entries[_index];
            string indexText = _entries.Count > 1 ? $", {_index + 1} of {_entries.Count}" : "";
            string stateText = "";
            bool? state = GetToggleState(entry.Button);
            if (state.HasValue) stateText = state.Value ? ", on" : ", off";
            Speech.SayImmediate($"{prefix}{entry.Label}{stateText}{indexText}");
        }

        private static bool? GetToggleState(SelectionButton button)
        {
            if (button == null) return null;
            try
            {
                var imageOn = button.transform.Find("ImageOn");
                var imageOff = button.transform.Find("ImageOff");
                if (imageOn == null || imageOff == null) return null;
                bool isOn = imageOn.gameObject.activeInHierarchy;

                // NotOwnButton's game-side meaning is "Show All Cards" — its ImageOn
                // means the filter is OFF. Invert so our label matches user intuition.
                if (button.gameObject.name == "NotOwnButton")
                    isOn = !isOn;

                return isOn;
            }
            catch { return null; }
        }

        private static void BuildEntries()
        {
            _entries.Clear();

            // Scope to the active dialog when one is open — otherwise the scene scan
            // would also pick up the underlying screen's icons that aren't reachable.
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ShortcutIcon> icons;
            var dialogRoot = FindActiveDialogRoot();
            if (dialogRoot != null)
                icons = dialogRoot.GetComponentsInChildren<ShortcutIcon>(false);
            else
                icons = UnityEngine.Object.FindObjectsOfType<ShortcutIcon>();
            if (icons == null) return;

            var seen = new HashSet<int>();
            for (int i = 0; i < icons.Count; i++)
            {
                var icon = icons[i];
                if (icon == null || !icon.gameObject.activeInHierarchy) continue;
                if (icon.keyType == SelectorManager.KeyType.None) continue;

                var btn = FindButtonAncestor(icon.transform);
                if (btn == null) continue;

                int id = btn.gameObject.GetInstanceID();
                if (!seen.Add(id)) continue; // dedupe — modifier icon points at same button

                string label = ResolveLabel(btn.gameObject.name);
                if (string.IsNullOrEmpty(label)) continue;

                _entries.Add(new Entry { Label = label, Button = btn });
            }
        }

        private static GameObject FindActiveDialogRoot()
        {
            if (string.IsNullOrEmpty(NavigationState.LastDialogTitle)) return null;
            var dialogManager = GameObject.Find("UI/OverlayCanvas/DialogManager");
            if (dialogManager == null) return null;
            for (int i = 0; i < dialogManager.transform.childCount; i++)
            {
                var dialogRoot = dialogManager.transform.GetChild(i);
                if (!dialogRoot.gameObject.activeInHierarchy) continue;
                for (int j = 0; j < dialogRoot.childCount; j++)
                {
                    var dialogUI = dialogRoot.GetChild(j);
                    if (!dialogUI.gameObject.activeInHierarchy) continue;
                    if (!dialogUI.name.Contains("(Clone)")) continue;
                    return dialogUI.gameObject;
                }
            }
            return null;
        }

        private static SelectionButton FindButtonAncestor(Transform t)
        {
            int depth = 0;
            while (t != null && depth < 5)
            {
                var btn = t.GetComponent<SelectionButton>();
                if (btn != null) return btn;
                t = t.parent;
                depth++;
            }
            return null;
        }

        private static string ResolveLabel(string buttonName)
        {
            // Labels for buttons that only exist in the virtual toolbar's view —
            // not reachable via normal focus, so they aren't in ButtonPatches.ParentLabels.
            switch (buttonName)
            {
                case "InputButton": return "Search";
                case "NotOwnButton": return "Hide unowned cards filter";
            }

            if (PatchColorContainerGraphic.ParentLabels.TryGetValue(buttonName, out var label))
                return label;
            if (string.IsNullOrEmpty(buttonName)) return null;
            if (buttonName.StartsWith("Button")) return buttonName.Substring(6);
            if (buttonName.EndsWith("Button")) return buttonName.Substring(0, buttonName.Length - 6);
            return buttonName;
        }

        private struct Entry
        {
            public string Label;
            public SelectionButton Button;
        }
    }

    [HarmonyPatch(typeof(ViewControllerManager), nameof(ViewControllerManager.OnBack))]
    class PatchOnBackForToolbar
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (VirtualToolbar.OnBackPressed()) { __result = true; return false; }
            return true;
        }
    }

    // Catches the "back with unsaved changes" path that bypasses OnBack.
    [HarmonyPatch(typeof(SaveDialogViewController), nameof(SaveDialogViewController.Open))]
    class PatchSaveDialogOpenForToolbar
    {
        [HarmonyPrefix]
        static bool Prefix() => !VirtualToolbar.OnBackPressed();
    }
}
