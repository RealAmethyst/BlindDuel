using System;
using Il2CppYgomSystem.UI;
using Il2CppYgomGame.Card;
using Il2CppYgomGame.CardPack.Open.Actor;
using UnityEngine;

namespace BlindDuel
{
    /// <summary>
    /// Handler for card pack opening and result screens.
    /// Announces card rarity when face-down, full card name when revealed.
    /// </summary>
    public class CardPackOpenHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "CardPackOpen" or "CardPackOpenResult";

        public bool OnScreenEntered(string viewControllerName)
        {
            if (viewControllerName == "CardPackOpenResult")
                Speech.AnnounceScreen("Pack Results");
            else
            {
                PackFlipTracker.Reset();
                Speech.AnnounceScreen("Pack Opening");
            }
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            // Suppress "Related Cards" button
            try
            {
                string btnText = TextExtractor.ExtractFirst(button.gameObject);
                if (btnText != null && btnText.Contains("Related"))
                    return "";
            }
            catch { }

            try
            {
                // Find the CardPackCardActor for this button
                var actor = FindActor(button);
                if (actor != null)
                {
                    int mrk = actor._mrk_k__BackingField;
                    bool isFaceUp = PackFlipTracker.IsFlipped(mrk);

                    if (isFaceUp && mrk > 0)
                    {
                        // Card is revealed - speak full name + rarity + new status
                        var card = CardReader.ReadCardFromData(mrk);
                        string name = card?.Name;
                        if (string.IsNullOrEmpty(name)) return null;

                        string result = name;

                        try
                        {
                            string rarityText = CardCollectionInfo.GetCardRarityText(
                                (int)CardCollectionInfo.GetCardRarity(mrk));
                            if (!string.IsNullOrEmpty(rarityText))
                                result += $", {rarityText}";
                        }
                        catch { }

                        return result;
                    }
                    else
                    {
                        return "Face-down card";
                    }
                }

                // Result screen
                int resultMrk = FindResultMrk(button, out bool resultIsNew);
                if (resultMrk > 0)
                {
                    var card = CardReader.ReadCardFromData(resultMrk);
                    string name = card?.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        string result = name;
                        try
                        {
                            string rarityText = CardCollectionInfo.GetCardRarityText(
                                (int)CardCollectionInfo.GetCardRarity(resultMrk));
                            if (!string.IsNullOrEmpty(rarityText))
                                result += $", {rarityText}";
                        }
                        catch { }

                        result += resultIsNew ? ", New" : ", Owned";
                        return result;
                    }
                }
            }
            catch (Exception ex) { Log.Write($"[CardPackOpen] {ex.Message}"); }

            return null;
        }

        private static CardPackCardActor FindActor(SelectionButton button) =>
            button.GetComponentInParent<CardPackCardActor>();

        private static int FindResultMrk(SelectionButton button, out bool isNew)
        {
            isNew = false;

            // Result screen hierarchy:
            //   PackCardTemplate(Clone) [parent]
            //     CardPict [button] - has BindingCardMaterial with m_CardId
            //     NewIcon - active if card is new
            //     IconRarity
            //     NumTextArea
            try
            {
                // Get card ID from BindingCardMaterial on the button itself
                var binding = button.GetComponent<Il2CppYgomGame.Menu.Common.BindingCardMaterial>();
                if (binding != null)
                {
                    int mrk = binding.m_CardId;
                    if (mrk > 0)
                    {
                        // NewIcon is a sibling under the same parent (PackCardTemplate)
                        var parent = button.transform.parent;
                        if (parent != null)
                        {
                            var newIcon = parent.Find("NewIcon");
                            if (newIcon != null)
                                isNew = newIcon.gameObject.activeInHierarchy;
                        }
                        return mrk;
                    }
                }
            }
            catch (Exception ex) { Log.Write($"[CardPackResult] {ex.Message}"); }

            return 0;
        }
    }
}
