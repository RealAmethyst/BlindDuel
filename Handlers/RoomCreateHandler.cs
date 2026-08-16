using System;
using Il2CppYgomGame.Room;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class RoomCreateHandler : IMenuHandler
    {
        // ON/OFF toggles flip in place without firing a focus change, so we track the
        // focused row's TextSetting value across frames to detect the flip.
        private static SelectionButton _lastFocused;
        private static string _lastSetting;

        public bool CanHandle(string viewControllerName) => viewControllerName == "RoomCreate";

        public bool OnScreenEntered(string viewControllerName)
        {
            _lastFocused = null;
            _lastSetting = null;

            string header = ScreenDetector.ReadGameHeaderText();
            string announcement = !string.IsNullOrWhiteSpace(header) ? header : "Create a Room";
            Speech.AnnounceScreen(announcement);
            return true;
        }

        public static void Poll()
        {
            try
            {
                if (HandlerRegistry.Current is not RoomCreateHandler)
                {
                    _lastFocused = null;
                    _lastSetting = null;
                    return;
                }

                var btn = SelectorManager.currentItem?.TryCast<SelectionButton>();
                if (btn == null || btn.WasCollected)
                {
                    _lastFocused = null;
                    _lastSetting = null;
                    return;
                }

                var vc = ScreenDetector.GetFocusVC<RoomCreateViewController>();
                if (vc == null) return;
                if (!TryGetRow(vc, btn, out var row)) return;

                string current = ReadChild(row, "TextSetting");

                if (btn != _lastFocused)
                {
                    _lastFocused = btn;
                    _lastSetting = current;
                    return;
                }

                if (!string.Equals(current, _lastSetting, StringComparison.Ordinal))
                {
                    _lastSetting = current;
                    if (!string.IsNullOrWhiteSpace(current))
                        Speech.SayImmediate(current);
                }
            }
            catch { }
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                if (button.name == "ButtonOK")
                {
                    string txt = TextExtractor.ExtractFirst(button.gameObject);
                    return !string.IsNullOrWhiteSpace(txt) ? txt : "Create Room";
                }

                var vc = ScreenDetector.GetFocusVC<RoomCreateViewController>();
                if (vc == null) return null;

                if (TryGetRow(vc, button, out var row))
                    return FormatSettingRow(row);
            }
            catch (Exception ex) { Log.Write($"[RoomCreateHandler] Button error: {ex.Message}"); }
            return null;
        }

        private static bool TryGetRow(RoomCreateViewController vc, SelectionButton button, out GameObject row)
        {
            row = null;
            var isv = vc.isv;
            if (isv == null) return false;

            return TransformSearch.TryResolveByAncestor<GameObject>(button.transform, 8,
                go =>
                {
                    try { return isv.GetDataIndexByEntity(go) >= 0 ? (true, go) : (false, null); }
                    catch { return (false, null); }
                },
                out row);
        }

        private static string FormatSettingRow(GameObject row)
        {
            string title = ReadChild(row, "TextTitle");
            string setting = ReadChild(row, "TextSetting");

            if (string.IsNullOrWhiteSpace(title))
                return null;
            if (string.IsNullOrWhiteSpace(setting))
                return title;
            return $"{title}, {setting}";
        }

        private static string ReadChild(GameObject root, string childName)
        {
            try
            {
                var t = TransformSearch.FindByName(root.transform, childName);
                if (t == null) return null;
                return TextExtractor.ExtractFirst(t.gameObject);
            }
            catch { return null; }
        }
    }
}
