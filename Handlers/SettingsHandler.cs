using System;
using System.Collections.Generic;
using Il2CppYgomGame.Settings;
using Il2CppYgomSystem.UI;
using Il2CppYgomSystem.YGomTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlindDuel
{
    public class SettingsHandler : IMenuHandler
    {
        private static SelectionButton _lastValueButton;
        private static float _lastSliderValue;
        private static string _lastModeText;

        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "SettingMenuViewController" or "GameSettingMenu";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            Speech.AnnounceScreen(!string.IsNullOrWhiteSpace(header) ? header : "Game Settings");
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            if (button.name == "CancelButton") return null;

            var info = TryGetPageInfo(button, out int index, out int total);
            if (info != null)
                return FormatCategory(info, index, total);

            return FormatSetting(button);
        }

        public static void PollSettingValue()
        {
            try
            {
                if (HandlerRegistry.Current is not SettingsHandler) { _lastValueButton = null; return; }

                var btn = SelectorManager.currentItem?.TryCast<SelectionButton>();
                if (btn == null || btn.WasCollected) { _lastValueButton = null; return; }

                var slider = btn.GetComponentInChildren<Slider>();
                var modeText = FindModeText(btn);
                if (slider == null && modeText == null) { _lastValueButton = null; return; }

                if (btn != _lastValueButton)
                {
                    _lastValueButton = btn;
                    _lastSliderValue = slider?.value ?? 0f;
                    _lastModeText = modeText?.text?.Trim() ?? "";
                    return;
                }

                if (slider != null)
                {
                    int current = (int)Math.Round(slider.value);
                    int last = (int)Math.Round(_lastSliderValue);
                    if (current != last)
                    {
                        _lastSliderValue = slider.value;
                        int max = (int)Math.Round(slider.maxValue);
                        Speech.SayImmediate($"{current} of {max}");
                    }
                }

                if (modeText != null)
                {
                    string current = modeText.text?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(current) && current != _lastModeText)
                    {
                        _lastModeText = current;
                        Speech.SayImmediate(current);
                    }
                }
            }
            catch { }
        }

        private static ExtendedTextMeshProUGUI FindModeText(SelectionButton btn)
        {
            var tmps = btn.GetComponentsInChildren<ExtendedTextMeshProUGUI>();
            if (tmps == null) return null;
            foreach (var tmp in tmps)
            {
                if (tmp != null && tmp.name == "ModeText" && tmp.gameObject.activeInHierarchy)
                    return tmp;
            }
            return null;
        }

        // --- Category handling (left panel tabs) ---

        /// <summary>
        /// Try to match the button against the VC's pageInfo dictionary.
        /// Returns the matching PageInfo plus 1-based index and total among visible pages.
        /// </summary>
        private static SettingMenuViewController.PageInfo TryGetPageInfo(
            SelectionButton button, out int index, out int total)
        {
            index = 0; total = 0;

            var vc = ScreenDetector.GetFocusVC<SettingMenuViewController>();
            if (vc?.pageInfo == null) return null;

            int buttonId = button.gameObject.GetInstanceID();
            var visible = new List<(int order, SettingMenuViewController.PageInfo info)>();
            var it = vc.pageInfo.GetEnumerator();
            while (it.MoveNext())
            {
                var kvp = it.Current;
                var info = kvp.Value;
                if (info?.button == null) continue;
                if (!info.button.gameObject.activeInHierarchy) continue;
                visible.Add(((int)kvp.Key, info));
            }
            visible.Sort((a, b) => a.order.CompareTo(b.order));
            total = visible.Count;

            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].info.button.gameObject.GetInstanceID() == buttonId)
                {
                    index = i + 1;
                    return visible[i].info;
                }
            }
            return null;
        }

        private static string FormatCategory(SettingMenuViewController.PageInfo info, int index, int total)
        {
            string name = ReadCategoryText(info);
            if (string.IsNullOrWhiteSpace(name)) return null;

            if (total > 1 && index > 0)
                return $"{name}, {index} of {total}";
            return name;
        }

        /// <summary>
        /// Read the tab's localized label from on/off GameObjects, preferring non-placeholder text.
        /// Both state containers hold a TextTMP; only one has the proper localized string.
        /// </summary>
        private static string ReadCategoryText(SettingMenuViewController.PageInfo info)
        {
            string on = ReadTMPTextInclusive(info.on);
            string off = ReadTMPTextInclusive(info.off);
            return PickLocalized(on, off);
        }

        private static string ReadTMPTextInclusive(GameObject root)
        {
            if (root == null) return null;
            var tmp = root.GetComponentInChildren<Il2CppTMPro.TMP_Text>(true);
            return tmp?.text?.Trim();
        }

        private static string PickLocalized(string a, string b)
        {
            bool aBad = IsPlaceholder(a);
            bool bBad = IsPlaceholder(b);
            if (!aBad && bBad) return a;
            if (aBad && !bBad) return b;
            return string.IsNullOrEmpty(a) ? b : a;
        }

        private static bool IsPlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            return text.Contains("(仮)");
        }

        // --- Setting handling (right panel) ---

        private static string FormatSetting(SelectionButton button)
        {
            string title = null;
            string modeTextValue = null;
            var descriptions = new List<string>();

            var tmps = button.GetComponentsInChildren<ExtendedTextMeshProUGUI>();
            if (tmps != null)
            {
                foreach (var tmp in tmps)
                {
                    if (tmp == null || !tmp.gameObject.activeInHierarchy) continue;
                    string text = tmp.text?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    if (tmp.name == "ModeText")
                        modeTextValue = text;
                    else if (title == null)
                        title = text;
                    else
                        descriptions.Add(text);
                }
            }

            string value = null;
            var slider = button.GetComponentInChildren<Slider>();
            if (slider != null)
            {
                int current = (int)Math.Round(slider.value);
                int max = (int)Math.Round(slider.maxValue);
                value = $"{current} of {max}";
            }
            else if (!string.IsNullOrEmpty(modeTextValue))
                value = modeTextValue;

            if (string.IsNullOrWhiteSpace(title)) return null;

            string result = title;
            if (!string.IsNullOrEmpty(value))
                result += $", {value}";
            foreach (var d in descriptions)
                result += $", {d}";

            var (index, total) = CountSettingIndex(button);
            if (total > 1 && index > 0)
                result += $", {index} of {total}";

            return result;
        }

        private static (int index, int total) CountSettingIndex(SelectionButton button)
        {
            Transform row = button.transform;
            Transform container = button.transform.parent;
            if (container == null) return (0, 0);

            Transform candidate = container;
            int best = -1;
            for (int level = 0; level < 3 && candidate != null; level++)
            {
                int count = 0;
                for (int i = 0; i < candidate.childCount; i++)
                {
                    var c = candidate.GetChild(i);
                    if (!c.gameObject.activeInHierarchy) continue;
                    if (c.GetComponentInChildren<SelectionButton>() != null)
                        count++;
                }
                if (count > best)
                {
                    best = count;
                    container = candidate;
                    Transform r = button.transform;
                    for (int up = 0; up < level; up++)
                        r = r.parent;
                    row = r;
                }
                candidate = candidate.parent;
            }

            if (best < 2) return (0, 0);

            int index = 0, total = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                var c = container.GetChild(i);
                if (!c.gameObject.activeInHierarchy) continue;
                if (c.GetComponentInChildren<SelectionButton>() == null) continue;
                total++;
                if (c == row) index = total;
            }
            return (index, total);
        }
    }
}
