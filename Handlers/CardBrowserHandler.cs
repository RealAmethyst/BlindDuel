using System;
using Il2CppYgomGame.Card;
using Il2CppYgomGame.CardBrowser;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class CardBrowserHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "CardBrowser" or "CardListBrowser";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            if (!string.IsNullOrEmpty(header))
            {
                // Suppress "Related Cards" text from pack opening card detail view
                if (header.Contains("Related") || header.Contains("related"))
                    return true;

                Speech.AnnounceScreen(header);
                return true;
            }
            return false;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            // Suppress "Related Cards" button from card detail view
            if (button.name.Contains("Related") || button.name.Contains("related"))
                return "";
            string btnText = TextExtractor.ExtractFirst(button.gameObject);
            if (btnText != null && btnText.Contains("Related"))
                return "";

            try
            {
                // CardListBrowser — InfinityScroll-based card list (pack contents, structure decks)
                var listBrowserVC = ScreenDetector.GetFocusVC<CardListBrowserViewController>();
                if (listBrowserVC != null)
                    return HandleCardListBrowser(button, listBrowserVC);
            }
            catch (Exception ex)
            {
                Log.Write($"[CardBrowserHandler] {ex.Message}");
            }
            return null;
        }

        private static string HandleCardListBrowser(SelectionButton button, CardListBrowserViewController vc)
        {
            var widgetDic = vc.m_CardWidgetDic;
            if (widgetDic == null) return null;

            // Walk up from button to find the CardWidget in the dictionary
            TransformSearch.TryResolveByAncestor<CardListBrowserViewController.CardWidget>(
                button.transform, 5,
                go => widgetDic.TryGetValue(go, out var widget) ? (true, widget) : (false, null),
                out var cardWidget);

            if (cardWidget == null) return null;

            int mrk = cardWidget.m_Mrk;
            if (mrk <= 0) return null;

            // Get card name from game's card database
            string name = Content.s_instance?.GetName(mrk);
            if (string.IsNullOrEmpty(name)) return null;

            string result = name;

            // Index — m_Idx is the data index set by InfinityScrollView
            int total = vc.m_CardMrks?.Count ?? 0;
            int idx = cardWidget.m_Idx + 1; // 0-based → 1-based
            if (total > 1)
                result += $"\n{idx} of {total}";

            return result;
        }
    }
}
