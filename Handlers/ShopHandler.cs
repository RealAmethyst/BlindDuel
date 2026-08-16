using System;
using System.Collections.Generic;
using Il2CppYgomGame.Shop;
using Il2CppYgomGame.Utility;
using Il2CppYgomSystem.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BlindDuel
{
    public class ShopHandler : IMenuHandler
    {
        // Track last focused category/subcategory/section to detect changes when scrolling products
        private int _lastCategoryId = -1;
        private int _lastSubCategoryId = -1;
        private int _lastSectionId = -1;
        // Track flat subtab index to suppress accordion double-fire; reset on non-subtab focus
        private int _lastSubTabIdx = -1;

        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "Shop" or "ShopMenu";

        public bool OnScreenEntered(string viewControllerName)
        {
            _lastCategoryId = -1;
            _lastSubCategoryId = -1;
            _lastSectionId = -1;
            _lastSubTabIdx = -1;
            string header = ScreenDetector.ReadGameHeaderText();
            string announcement = header ?? "Shop";

            try
            {
                var shopVC = ScreenDetector.GetFocusVC<ShopViewController>();
                var data = shopVC?.m_ShowcaseData;
                if (data != null)
                {
                    int catId = data.currentCategoryId;
                    var catData = data.GetCategoryData(catId);
                    string tabName = catData?.labelText;
                    if (!string.IsNullOrEmpty(tabName))
                        announcement += $", {tabName}";
                }
            }
            catch { }

            Speech.AnnounceScreen(announcement);
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var shopVC = ScreenDetector.GetFocusVC<ShopViewController>();

                // Check main category tabs
                if (IsMainTab(button, shopVC))
                {
                    _lastSubTabIdx = -1;
                    return HandleCategoryTab(button, shopVC);
                }

                // Check subcategory tabs
                if (IsSubTab(button, shopVC))
                    return HandleSubcategoryTab(button, shopVC);

                // Not a subtab — reset so re-focusing the same subtab later will speak
                _lastSubTabIdx = -1;

                // Check products
                var (productWidget, productIdx, productTotal) = FindProduct(button, shopVC);
                if (productWidget != null)
                    return HandleProduct(productWidget, productIdx, productTotal, shopVC);

                // Card in pack
                if (button.name == "CardPict")
                    return HandleCardInPack(button);

                return null;
            }
            catch (Exception ex)
            {
                Log.Write($"[ShopHandler] {ex.Message}");
                return null;
            }
        }

        private string HandleCategoryTab(SelectionButton button, ShopViewController shopVC)
        {
            string text = TextExtractor.ExtractFirst(button.gameObject);
            if (string.IsNullOrEmpty(text)) return null;

            string result = $"{text}, tab";

            try
            {
                var data = shopVC?.m_ShowcaseData;
                if (data != null)
                {
                    int index = data.currentCategoryIdx + 1;
                    int total = data.GetCategoriesLength();
                    if (total > 1)
                        result += $", {index} of {total}";
                }
            }
            catch { }

            return result;
        }

        private string HandleSubcategoryTab(SelectionButton button, ShopViewController shopVC)
        {
            try
            {
                var data = shopVC?.m_ShowcaseData;
                if (data == null) return null;

                int catId = data.currentCategoryId;
                int subCatId = data.currentSubCategoryId;
                int sectionsCount = data.GetSectionsLength(catId, subCatId);
                int curSectionIdx = sectionsCount > 0 ? data.currentSectionIdx : 0;

                // Compute true flat index/total across all subcategories and their sections
                var subCatIds = data.GetSubcategories(catId);
                if (subCatIds == null) return null;

                int flatIdx = 0;
                int flatTotal = 0;
                for (int i = 0; i < subCatIds.Count; i++)
                {
                    int scId = subCatIds[i];
                    int secLen = data.GetSectionsLength(catId, scId);
                    int count = secLen > 0 ? secLen : 1;

                    if (scId == subCatId)
                        flatIdx = flatTotal + (secLen > 0 ? curSectionIdx + 1 : 1);

                    flatTotal += count;
                }

                // Suppress accordion double-fire (same flat position, reset by non-subtab focus)
                if (flatIdx == _lastSubTabIdx)
                    return "";
                _lastSubTabIdx = flatIdx;

                // Get label from game data
                string text = null;
                if (sectionsCount > 0)
                {
                    int sectionId = data.currentSectionId;
                    text = data.GetSectionData(catId, subCatId, sectionId)?.labelText;
                }
                else
                {
                    text = data.GetSubCategoryData(catId, subCatId)?.labelText;
                }

                if (string.IsNullOrEmpty(text))
                    text = TextExtractor.ExtractFirst(button.gameObject);
                if (string.IsNullOrEmpty(text)) return null;

                _lastCategoryId = catId;
                _lastSubCategoryId = subCatId;

                string result = $"{text}, subtab, {flatIdx} of {flatTotal}";

                return result;
            }
            catch { }

            // Fallback if game data unavailable
            string fallback = TextExtractor.ExtractFirst(button.gameObject);
            return !string.IsNullOrEmpty(fallback) ? $"{fallback}, subtab" : null;
        }

        private string HandleProduct(ProductWidget widget, int index, int total, ShopViewController shopVC)
        {
            // Use ProductContext.productName for full (non-truncated) name, fall back to UI text
            var ctx = widget.productContext;
            string name = ctx?.productName;
            if (string.IsNullOrEmpty(name))
                name = widget.nameText?.text?.Trim();
            if (string.IsNullOrEmpty(name))
                name = TextExtractor.ExtractFirst(widget.button?.gameObject);
            if (string.IsNullOrEmpty(name)) return null;

            // Detect category/subcategory/section change — prepend to result
            // so it's one unbreakable utterance (separate SayItem gets cut off on fast nav)
            string sectionPrefix = null;
            try
            {
                var data = shopVC?.m_ShowcaseData;
                if (data != null)
                {
                    int catId = data.currentCategoryId;
                    int subCatId = data.currentSubCategoryId;
                    int secId = data.currentSectionId;

                    if (_lastCategoryId >= 0 && catId != _lastCategoryId)
                    {
                        var catData = data.GetCategoryData(catId);
                        string catName = catData?.labelText;

                        int sectionsCount = data.GetSectionsLength(catId, subCatId);
                        string subName = sectionsCount > 0
                            ? data.GetSectionData(catId, subCatId, secId)?.labelText
                            : data.GetSubCategoryData(catId, subCatId)?.labelText;

                        if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(subName))
                            sectionPrefix = $"{catName}, {subName}";
                        else if (!string.IsNullOrEmpty(catName))
                            sectionPrefix = catName;
                        else if (!string.IsNullOrEmpty(subName))
                            sectionPrefix = subName;
                    }
                    else if (_lastSubCategoryId >= 0 && subCatId != _lastSubCategoryId)
                    {
                        int sectionsCount = data.GetSectionsLength(catId, subCatId);
                        sectionPrefix = sectionsCount > 0
                            ? data.GetSectionData(catId, subCatId, secId)?.labelText
                            : data.GetSubCategoryData(catId, subCatId)?.labelText;
                    }
                    else if (_lastSectionId >= 0 && secId != _lastSectionId)
                    {
                        int sectionsCount = data.GetSectionsLength(catId, subCatId);
                        if (sectionsCount > 0)
                            sectionPrefix = data.GetSectionData(catId, subCatId, secId)?.labelText;
                    }

                    _lastCategoryId = catId;
                    _lastSubCategoryId = subCatId;
                    _lastSectionId = secId;
                }
            }
            catch { }

            string result = !string.IsNullOrEmpty(sectionPrefix) ? $"{sectionPrefix}, {name}" : name;

            // Product type label (distinguishes packs, structure decks, accessories, etc.)
            string headLabel = ctx?.headLabelText;
            if (!string.IsNullOrEmpty(headLabel))
                result += $"\n{headLabel}";

            // Sold out
            if (ctx != null && ctx.isSoldOut)
                result += "\nSold out";

            // Pack pickup message (featured card name shown on pack products)
            try
            {
                var pickupGroup = widget.packPickupMessageGroup;
                if (pickupGroup != null && pickupGroup.activeInHierarchy)
                {
                    string pickup = widget.packPickupMessage?.text?.Trim();
                    if (!string.IsNullOrEmpty(pickup))
                        result += $"\n{pickup}";
                }
            }
            catch { }

            // Time remaining — limitRemainText holds the countdown ("29 day(s) left"),
            // NOT the purchase count. "00:00" means no time limit.
            string limitRemain = TextUtil.StripCJK(widget.limitRemainText?.text?.Trim());
            if (!string.IsNullOrEmpty(limitRemain) && limitRemain != "00:00")
                result += $"\n{limitRemain}";

            // Free pulls (only show when the free price element is actually visible)
            var freeTextObj = widget.priceFreeText;
            if (freeTextObj != null && freeTextObj.gameObject.activeInHierarchy)
            {
                string freeText = TextUtil.StripCJK(freeTextObj.text?.Trim());
                if (!string.IsNullOrEmpty(freeText))
                    result += $"\nFree: {freeText}";
            }

            // Price — use PriceContext for accurate currency type
            // payCat=1 (CONSUME) = Gems, anything else = ticket/special currency
            // ItemUtil.GetItemName resolves the localized currency name from payItemCategory + payItemId
            try
            {
                var priceCtx = ctx?.listButtonPrice;
                if (priceCtx != null && priceCtx.priceAmount > 0)
                {
                    int amount = priceCtx.priceAmount;
                    int payCat = priceCtx.payItemCategory;

                    if (payCat == 1) // CONSUME = Gems
                    {
                        result += $"\nPrice: {amount} Gems";
                    }
                    else // Tickets / special currency — resolve name from game data
                    {
                        string currencyName = null;
                        try
                        {
                            currencyName = TextUtil.StripCJK(
                                ItemUtil.GetItemName(priceCtx.payItemIsPeriod, payCat, priceCtx.payItemId));
                        }
                        catch { }

                        // Normalize plural: game returns inconsistent singular/plural
                        if (!string.IsNullOrEmpty(currencyName))
                        {
                            if (amount == 1 && currencyName.EndsWith("s"))
                                currencyName = currencyName.Substring(0, currencyName.Length - 1);
                            else if (amount > 1 && !currencyName.EndsWith("s"))
                                currencyName += "s";
                        }

                        result += !string.IsNullOrEmpty(currencyName)
                            ? $"\nPrice: {amount} {currencyName}"
                            : $"\nPrice: {amount} Tickets";
                    }
                }
            }
            catch
            {
                // Fallback to UI text
                string price = widget.priceText?.text?.Trim();
                if (!string.IsNullOrEmpty(price))
                    result += $"\nPrice: {price}";
            }

            if (total > 1 && index > 0)
                result += $"\n{index} of {total}";

            return result;
        }

        private string HandleCardInPack(SelectionButton button)
        {
            var numArea = button.transform.parent?.Find("NumTextArea");
            string ownedText = numArea != null ? TextExtractor.ExtractFirst(numArea.gameObject) : "";
            if (!string.IsNullOrEmpty(ownedText)) ownedText = "x" + ownedText[1..];

            var rarityIcon = button.transform.parent?.Find("IconRarity");
            string rarity = "";
            if (rarityIcon != null)
                rarity = EnumUtil.ParseRarity(rarityIcon.GetComponent<Image>()?.sprite?.name);

            var newIcon = button.transform.parent?.Find("NewIcon");
            bool isNew = newIcon != null && newIcon.gameObject.activeInHierarchy;

            return $"Rarity: {rarity}, New: {(isNew ? "Yes" : "No")}, Owned: {ownedText}";
        }

        /// <summary>
        /// Check if the button belongs to a main category tab.
        /// </summary>
        private static bool IsMainTab(SelectionButton button, ShopViewController shopVC)
        {
            try
            {
                var tabMap = shopVC?.m_MainTabList?.m_TabWidgetMap;
                if (tabMap == null) return false;

                var enumerator = tabMap.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Value?.button == button)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if the button belongs to the subcategory tab area by walking up
        /// the hierarchy to see if it's a descendant of the subtab container.
        /// </summary>
        private static bool IsSubTab(SelectionButton button, ShopViewController shopVC)
        {
            try
            {
                var tabMap = shopVC?.m_SubTabList?.m_TabWidgetMap;
                if (tabMap == null) return false;

                // Find the subtab container (parent of top-level tab entities)
                Transform container = null;
                var enumerator = tabMap.GetEnumerator();
                if (enumerator.MoveNext())
                    container = enumerator.Current.Key?.transform?.parent;
                if (container == null) return false;

                // Check if button is a descendant of this container
                Transform t = button.transform.parent;
                while (t != null)
                {
                    if (t == container) return true;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Find the ProductWidget for a button and determine its global index/total
        /// by matching its shopId against the ordered m_ProductContainerCtxs list.
        /// </summary>
        private static (ProductWidget widget, int index, int total) FindProduct(SelectionButton button, ShopViewController shopVC)
        {
            try
            {
                // Find the widget from the container map
                var containerMap = shopVC?.m_ProductList?.m_ContainerWidgetMap;
                if (containerMap == null) return (null, 0, 0);

                ProductWidget matchedWidget = null;
                var enumerator = containerMap.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var widgets = enumerator.Current.Value?.productWidgets;
                    if (widgets == null) continue;

                    for (int i = 0; i < widgets.Count; i++)
                    {
                        if (widgets[i]?.button == button)
                        {
                            matchedWidget = widgets[i];
                            break;
                        }
                    }
                    if (matchedWidget != null) break;
                }

                if (matchedWidget == null) return (null, 0, 0);

                // Get the product's shopId for position lookup
                int targetShopId = matchedWidget.productContext?.shopId ?? -1;
                if (targetShopId < 0) return (matchedWidget, 0, 0);

                // Walk the ordered container contexts to find global position and total
                var ctxs = shopVC?.m_ProductList?.m_ProductContainerCtxs;
                if (ctxs == null) return (matchedWidget, 0, 0);

                int total = 0;
                int matchIdx = -1;

                for (int c = 0; c < ctxs.Count; c++)
                {
                    var products = ctxs[c]?.productCtxs;
                    if (products == null) continue;

                    for (int p = 0; p < products.Count; p++)
                    {
                        total++;
                        if (matchIdx < 0 && products[p]?.shopId == targetShopId)
                            matchIdx = total;
                    }
                }

                return (matchedWidget, matchIdx > 0 ? matchIdx : 0, total);
            }
            catch { return (null, 0, 0); }
        }
    }
}
