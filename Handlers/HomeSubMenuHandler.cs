using System;
using Il2CppYgomGame.SubMenu;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class HomeSubMenuHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "HomeSubMenu" or "HomeSubMenuViewController";

        public bool OnScreenEntered(string viewControllerName) => true;

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var vc = HomeSubMenuState.CurrentInstance;
                if (vc == null) return null;

                var scroll = vc.m_InfinityScrollView;
                var templates = vc.m_Templates;
                var texts = vc.m_Texts;
                if (scroll == null || templates == null || texts == null) return null;

                int titleTNo = vc.k_TitleTNo;
                int itemTNo = vc.k_ItemTNo;
                int safeMax = Math.Min(templates.Count, texts.Count);

                // Walk up from the button to find the row entity the scroll view recognizes
                int dataIndex = TransformSearch.TryResolveByAncestor<int>(button.transform, 6,
                    go =>
                    {
                        int idx = scroll.GetDataIndexByEntity(go);
                        return idx >= 0 && idx < safeMax ? (true, idx) : (false, 0);
                    },
                    out int found) ? found : -1;

                string itemText = TextExtractor.ExtractFirst(button.gameObject);
                if (string.IsNullOrWhiteSpace(itemText) && dataIndex >= 0)
                    itemText = texts[dataIndex];
                if (string.IsNullOrWhiteSpace(itemText)) return null;

                string section = null;
                int itemIndex = 0;
                int totalItems = 0;

                if (dataIndex >= 0)
                {
                    for (int i = dataIndex; i >= 0; i--)
                    {
                        if (templates[i] == titleTNo)
                        {
                            section = texts[i];
                            break;
                        }
                    }
                }

                for (int i = 0; i < safeMax; i++)
                {
                    if (templates[i] == itemTNo)
                    {
                        totalItems++;
                        if (i == dataIndex)
                            itemIndex = totalItems;
                    }
                }

                string result = itemText;
                if (itemIndex > 0 && totalItems > 1)
                    result += $", {itemIndex} of {totalItems}";

                if (!string.IsNullOrWhiteSpace(section) && section != HomeSubMenuState.LastSpokenSection)
                {
                    HomeSubMenuState.LastSpokenSection = section;
                    result = $"{section},\n{result}";
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[HomeSubMenu] {ex.Message}");
                return null;
            }
        }
    }
}
