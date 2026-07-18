using System;
using Il2CppYgomSystem.UI;
using Il2CppYgomGame.GemShop;
using Il2CppYgomGame.Menu;
using UnityEngine;

namespace BlindDuel
{
    public class GemShopHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) => viewControllerName == "GemShop";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            Speech.AnnounceScreen(header ?? "Gem Shop");
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var widget = FindProductWidget(button);
                if (widget == null) return null;

                // Pack name
                string name = widget.productName?.text?.Trim();
                if (string.IsNullOrEmpty(name)) return null;

                string result = name;

                // Paid/Free gem breakdown from item widgets
                try
                {
                    var itemMap = widget.m_ItemWidgetMap;
                    if (itemMap != null)
                    {
                        ProductWidget.ItemWidget paidWidget = null;
                        ProductWidget.ItemWidget freeWidget = null;
                        itemMap.TryGetValue(0, out paidWidget);
                        itemMap.TryGetValue(1, out freeWidget);

                        string paidNum = paidWidget?.itemNumText?.text?.Trim();
                        string freeNum = freeWidget?.itemNumText?.text?.Trim();

                        if (!string.IsNullOrEmpty(paidNum))
                            result += $"\n{paidNum} purchased";
                        if (!string.IsNullOrEmpty(freeNum))
                            result += $"\n{freeNum} free";
                    }
                }
                catch { }

                // Purchase limit (e.g., "3 times remaining", "One-time opportunity!")
                string limitText = TextUtil.StripCJK(widget.limitCountText?.text?.Trim());
                if (!string.IsNullOrEmpty(limitText))
                    result += $"\n{limitText}";

                // Pop icon label (e.g., "One-time opportunity!")
                try
                {
                    var popRoot = widget.popIconRoot;
                    if (popRoot != null && popRoot.activeInHierarchy)
                    {
                        string popText = TextUtil.StripCJK(widget.popIconLabel?.text?.Trim());
                        if (!string.IsNullOrEmpty(popText))
                            result += $"\n{popText}";
                    }
                }
                catch { }

                // Time limit (e.g., "29 day(s) left")
                try
                {
                    var dateRoot = widget.limitDateRoot;
                    if (dateRoot != null && dateRoot.activeInHierarchy)
                    {
                        string dateText = widget.limitDateText?.text?.Trim();
                        if (!string.IsNullOrEmpty(dateText))
                            result += $"\n{dateText}";
                    }
                }
                catch { }

                // Price
                string price = TextUtil.StripCJK(widget.priceLabel?.text?.Trim());
                if (!string.IsNullOrEmpty(price))
                    result += $"\nPrice: {price}";

                // Index from game data
                int idx = widget.idx + 1; // 0-based → 1-based
                int total = GetProductCount();
                if (total > 1)
                    result += $"\n{idx} of {total}";

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[GemShopHandler] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find the ProductWidget for a button by looking up the GemShopViewController's entity map.
        /// </summary>
        private static ProductWidget FindProductWidget(SelectionButton button)
        {
            try
            {
                var gemShopVC = ScreenDetector.GetFocusVC<GemShopViewController>();
                if (gemShopVC == null) return null;

                var entityMap = gemShopVC.m_EntityWidgetMap;
                if (entityMap == null) return null;

                // Walk up from button to find the mapped GameObject (max 5 levels, as before).
                TransformSearch.TryResolveByAncestor<ProductWidget>(
                    button.transform, 5,
                    go => entityMap.TryGetValue(go, out var widget) ? (true, widget) : (false, null),
                    out var result);
                return result;
            }
            catch { }
            return null;
        }

        private static int GetProductCount()
        {
            try
            {
                var gemShopVC = ScreenDetector.GetFocusVC<GemShopViewController>();
                return gemShopVC?.m_ProductContexts?.Count ?? 0;
            }
            catch { return 0; }
        }
    }
}
