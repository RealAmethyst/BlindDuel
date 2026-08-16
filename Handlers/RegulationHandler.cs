using System;
using Il2CppYgomGame.Card;
using Il2CppYgomGame.Regulation;
using Il2CppYgomSystem.UI;
using Il2CppYgomSystem.UI.ElementWidget;
using UnityEngine;

namespace BlindDuel
{
    public class RegulationHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "CardListBrowserRegulationFilter"
                              or "CardListBrowserRegulationFilterViewController";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            if (!string.IsNullOrEmpty(header))
            {
                Speech.AnnounceScreen(header);
                return true;
            }
            return false;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var regVC = ScreenDetector.GetFocusVC<CardListBrowserRegulationFilterViewController>();
                if (regVC == null) return null;

                // Try card widget first
                var cardText = HandleCardWidget(button, regVC);
                if (cardText != null) return cardText;

                // Try filter tab
                var tabText = HandleFilterTab(button, regVC);
                if (tabText != null) return tabText;
            }
            catch (Exception ex)
            {
                Log.Write($"[RegulationHandler] {ex.Message}");
            }
            return null;
        }

        private static string HandleCardWidget(SelectionButton button, CardListBrowserRegulationFilterViewController regVC)
        {
            var widgetDic = regVC.m_CardWidgetDic;
            if (widgetDic == null) return null;

            // Walk up from button to find the CardWidget in the dictionary
            TransformSearch.TryResolveByAncestor<CardListBrowserRegulationFilterViewController.CardWidget>(
                button.transform, 5,
                go => widgetDic.TryGetValue(go, out var widget) ? (true, widget) : (false, null),
                out var cardWidget);

            if (cardWidget == null) return null;

            int mrk = cardWidget.m_Mrk;
            if (mrk <= 0) return null;

            string name = Content.s_instance?.GetName(mrk);
            if (string.IsNullOrEmpty(name)) return null;

            string result = name;

            int total = regVC.m_DisplayCardMrks?.Count ?? 0;
            int idx = cardWidget.m_Idx + 1;
            if (total > 1)
                result += $"\n{idx} of {total}";

            return result;
        }

        private static string HandleFilterTab(SelectionButton button, CardListBrowserRegulationFilterViewController regVC)
        {
            var filterForm = regVC.m_FilterFormWidget;
            if (filterForm == null) return null;

            var tabGroup = filterForm.tabGroup;
            if (tabGroup == null) return null;

            var tabEoms = tabGroup.m_TabEoms;
            var tabWidgetMap = tabGroup.m_TabWidgetMap;
            if (tabEoms == null || tabWidgetMap == null) return null;

            // Find which tab this button belongs to
            int tabIndex = -1;
            for (int i = 0; i < tabEoms.Count; i++)
            {
                var eom = tabEoms[i];
                if (eom == null) continue;
                if (!tabWidgetMap.TryGetValue(eom, out var toggle)) continue;
                if (toggle?.button?.Pointer == button.Pointer)
                {
                    tabIndex = i;
                    break;
                }
            }

            if (tabIndex < 0) return null;

            // Each tab has ImageOn/ImageOff children. On first visit, ImageOn may have
            // a dev placeholder containing 仮 (U+4EEE). Use ActiveOnly=false since
            // the correct text may be on the inactive image state.
            var results = TextExtractor.ExtractAll(tabEoms[tabIndex].gameObject,
                new TextSearchOptions { ActiveOnly = false, FilterBanned = false });

            string text = null;
            foreach (var r in results)
            {
                if (!string.IsNullOrEmpty(r.Text) && !r.Text.Contains("\u4EEE"))
                {
                    text = r.Text;
                    break;
                }
            }

            // Fallback if all texts are placeholders
            if (string.IsNullOrEmpty(text) && results.Count > 0)
                text = results[0].Text;

            if (string.IsNullOrEmpty(text)) return null;

            int tabCount = tabEoms.Count;
            if (tabCount > 1)
                text += $"\n{tabIndex + 1} of {tabCount}";

            return text;
        }
    }
}
