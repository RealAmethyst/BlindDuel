using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using Il2CppYgomGame.Duel;
using Il2CppYgomSystem.UI;
using Il2CppYgomSystem.YGomTMPro;
using HarmonyLib;

namespace BlindDuel
{
    [HarmonyPatch(typeof(ColorContainerGraphic), nameof(ColorContainerGraphic.SetColor))]
    class PatchColorContainerGraphic
    {
        // parent.parent name → label
        private static readonly Dictionary<string, string> ParentLabels = new()
        {
            { "ButtonMaintenance", "Maintenance" },
            { "ButtonBug", "Issues" },
            { "ButtonNotification", "Notification" },
            { "AutoBuildButton", "Auto-build button" },
            { "ButtonBookmark", "Add card to bookmark button" },
            { "BookmarkButton", "Bookmarked cards button" },
            { "HowToGetButton", "How to get button" },
            { "RelatedCard", "Related cards button" },
            { "AddButton", "Add +1" },
            { "RemoveButton", "Remove -1" },
            { "CardListButton", "Card list button" },
            { "HistoryButton", "Card history button" },
            { "ButtonRegulation", "Regulation button" },
            { "ButtonSecretPack", "Secret pack button" },
            { "ButtonInfoSwitching", "Switch display mode button" },
            { "ButtonSave", "Save button" },
            { "ButtonMenu", "Menu button" },
            { "ButtonPickupCard", "Show cards on decks preview" },
            { "BulkDecksDeletionButton", "Bulk deck deletion button" },
            { "ButtonOpenNeuronDecks", "Link with Yu Gi Oh Database" },
            { "FilterButton", "Filters button" },
            { "SortButton", "Sort button" },
            { "ClearButton", "Clear filters button" },
            { "ButtonDismantleIncrement", "Increment dismantle amount" },
            { "ButtonDismantleDecrement", "Decrement dismantle amount" },
            { "ButtonEnter", "Play" },
            { "CopyButton", "Copy deck button" },
            { "OKButton", "Ok" },
            { "ShowOwnedNumToggle", "Show owned button" },
        };

        // parent.parent.parent name → label
        private static readonly Dictionary<string, string> GrandparentLabels = new()
        {
            { "TabMyDeck", "My Deck" },
            { "TabRental", "Loaner" },
            { "DuelMenuButton", "Menu button" },
        };

        [HarmonyPostfix]
        static void Postfix(ColorContainerGraphic __instance)
        {
            try
            {
                if (__instance.currentStatusMode != ColorContainer.StatusMode.Enter) return;

                var parent = __instance.transform.parent.parent;
                var grandparent = parent.parent;
                string parentName = parent.name;
                string grandparentName = grandparent.name;

                // Duel card list — SetDescriptionArea fires natively when the game
                // focuses a card. No need to Click() here (it would fire for ALL
                // cards during list initialization, reading every card aloud).
                if (NavigationState.IsInDuel && parentName.Contains("DuelListCard"))
                    return;

                string text = null;

                // Dynamic cases needing computed text
                if (parentName is "DismantleButton" or "CreateButton")
                {
                    var costChild = TransformSearch.GetChild(parent, 6, "Dismantle/Create");
                    if (costChild != null)
                    {
                        string costText = TextExtractor.ExtractFirst(costChild.gameObject);
                        string rarity = EnumUtil.ParseRarity(costChild.GetComponentInChildren<Image>().sprite.name);

                        if (parentName == "DismantleButton" && string.IsNullOrEmpty(costText))
                            text = "Cant be dismantled";
                        else
                            text = $"{(parentName == "DismantleButton" ? "Dismantle" : "Create")} card for: {costText} {rarity} cp";
                    }
                }
                else if (parentName == "InputButton")
                {
                    text = NavigationState.CurrentMenu == Menu.None ? "Rename button/input" : "Search card input";
                }
                else if (parentName is "Button0")
                {
                    text = $"{TextExtractor.ExtractFirst(grandparent.gameObject)}, lower to higher";
                }
                else if (parentName is "Button1")
                {
                    text = $"{TextExtractor.ExtractFirst(grandparent.gameObject)}, higher to lower";
                }
                else if (ParentLabels.TryGetValue(parentName, out string label))
                {
                    text = label;
                }

                // Grandparent-level overrides
                if (grandparentName == "ChapterDuel(Clone)")
                {
                    var starsChild = TransformSearch.GetChild(parent, 4, "ChapterDuel/stars");
                    if (starsChild != null)
                        text = $"Duel, {TextExtractor.ExtractFirst(starsChild.gameObject)} stars";
                }
                else if (GrandparentLabels.TryGetValue(grandparentName, out string gpLabel))
                {
                    text = gpLabel;
                }

                if (!string.IsNullOrEmpty(text))
                    Speech.SayItem(text);
            }
            catch (Exception ex) { Log.Write($"[ColorContainer] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(SelectionButton), nameof(SelectionButton.OnSelected), MethodType.Normal)]
    class PatchOnSelected
    {
        /// <summary>
        /// Read item name + count from dialog item templates (works across all screens).
        /// Handles "Item Obtained" and similar dialogs with Template(Clone)/ItemNameText pattern.
        /// </summary>
        static string TryReadDialogItem(Transform buttonTransform)
        {
            try
            {
                Transform current = buttonTransform;
                for (int i = 0; i < 5 && current != null; i++)
                {
                    var tmps = current.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>();
                    string itemName = null;
                    string itemNum = null;

                    foreach (var tmp in tmps)
                    {
                        if (tmp.gameObject.name == "ItemNameText")
                            itemName = tmp.text?.Trim();
                        else if (tmp.gameObject.name == "ItemNumText")
                            itemNum = tmp.text?.Trim();
                    }

                    if (!string.IsNullOrEmpty(itemName))
                    {
                        if (!string.IsNullOrEmpty(itemNum))
                        {
                            itemNum = itemNum.TrimStart('×', 'x', 'X', ' ');
                            return $"{itemName} x{itemNum}";
                        }
                        return itemName;
                    }
                    current = current.parent;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Search sibling elements for text (handles toggle/radio widgets).
        /// </summary>
        static string FindSiblingText(Transform start)
        {
            var current = start;
            for (int level = 0; level < 3 && current.parent != null; level++)
            {
                var parent = current.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    if (sibling == current) continue;
                    var tmp = sibling.GetComponent<TMP_Text>();
                    if (tmp != null && tmp.gameObject.activeInHierarchy)
                    {
                        string clean = TextUtil.StripTags(tmp.text ?? "").Trim();
                        if (!string.IsNullOrEmpty(clean))
                            return clean;
                    }
                }
                current = current.parent;
            }
            return null;
        }

        [HarmonyPostfix]
        static void Postfix(SelectionButton __instance, bool __result)
        {
            try
            {
                // OnSelected returns false when the button was already selected — skip duplicate fires
                if (!__result) return;

                // Duel log handles its own navigation via PollSelection
                if (DuelState.IsDuelLogOpen) return;

                // Same button re-fired (rapid deselect/reselect) — skip to prevent
                // handler state mutation producing different text on second fire.
                // Exception: card selection lists recycle button objects (object pooling),
                // so the same reference can represent a different card after scrolling.
                if (Speech.IsSameButton(__instance))
                {
                    try
                    {
                        if (__instance.GetComponentInParent<Il2CppYgomGame.Duel.CardSelectionList>() == null)
                            return;
                    }
                    catch { return; }
                }

                // Screen announcement pending — capture the button for QueueFocusedItem
                // instead of speaking now (avoids speaking items before the header)
                if (ScreenDetector.HasPendingScreen)
                {
                    ScreenDetector.DeferFocusedButton(__instance);
                    return;
                }

                // Selection list pending — defer button until title is spoken.
                // Mirrors HasPendingScreen but for duel CardSelectionList prompts.
                if (DuelState.HasPendingSelection)
                {
                    DuelState.DeferredSelectionButton = __instance;
                    return;
                }

                // After CardCommand closes, suppress auto-focused button so the
                // selection prompt message speaks first without a blip.
                if (DuelState.SuppressNextFieldFocus && NavigationState.IsInDuel)
                {
                    DuelState.SuppressNextFieldFocus = false;
                    DuelState.DeferredSelectionButton = __instance;
                    return;
                }

                // Game-native transition check: suppress buttons that fire during
                // screen transitions (e.g. brief OK button during gate entry).
                // This catches transition artifacts that HasPendingScreen misses
                // because our Poll() hasn't detected the VC change yet.
                if (!ScreenDetector.IsScreenReady()) return;

                // Extract text from button hierarchy
                string text = TextExtractor.ExtractFirst(__instance.gameObject);

                // Fallback: sibling text for toggle/radio widgets
                if (string.IsNullOrWhiteSpace(text))
                    text = FindSiblingText(__instance.transform) ?? text;

                // InputDigit dialog +/- buttons: replace the symbolic "-" / "＋" label
                // with a spoken word so the screen reader doesn't fuse it with the
                // sibling-index suffix into a misleading negative number.
                string digitLabel = InputDigitWatcher.GetButtonLabel(__instance);
                if (digitLabel != null) text = digitLabel;

                // Let the active handler enhance the text
                string enhanced = null;
                var handler = HandlerRegistry.Current;
                if (handler != null)
                {
                    enhanced = handler.OnButtonFocused(__instance);
                    if (enhanced != null)
                        text = enhanced;
                }

                // Dialog item list fallback: any screen's dialog with ItemNameText/ItemNumText
                if (enhanced == null)
                {
                    string dialogItem = TryReadDialogItem(__instance.transform);
                    if (dialogItem != null)
                    {
                        text = dialogItem;
                        enhanced = dialogItem;
                    }
                }

                if (string.IsNullOrWhiteSpace(text)) return;

                // If handler didn't provide text, use generic index as fallback
                if (enhanced == null)
                {
                    var (index, total) = TransformSearch.GetButtonIndex(__instance);
                    if (total > 1)
                        text += $"\n{index} of {total}";
                }

                // After a screen/dialog/duel-message announcement, queue the auto-focused button
                // instead of interrupting the preceding speech.
                bool shouldQueue = NavigationState.DialogJustAnnounced
                                || NavigationState.ScreenJustAnnounced
                                || DuelState.MessageJustAnnounced;
                if (shouldQueue)
                {
                    NavigationState.DialogJustAnnounced = false;
                    NavigationState.ScreenJustAnnounced = false;
                    DuelState.MessageJustAnnounced = false;
                }

                if (NavigationState.IsInDuel)
                {
                    // Defer all duel buttons by one frame. This ensures dialogs/prompts
                    // that fire in the same frame (e.g. "Select battle position") speak
                    // BEFORE the auto-focused button, not after.
                    // If no dialog fires, Update() speaks it on the next frame (~16ms).
                    DuelState.LastQueuedButtonText = text;
                    DuelState.LastQueuedButtonFrame = UnityEngine.Time.frameCount;
                    DuelState.LastQueuedButtonInterrupt = !shouldQueue;
                    return;
                }

                if (shouldQueue)
                    Speech.SayQueued(text);
                else
                    Speech.SayItem(text);
            }
            catch (Exception ex)
            {
                Log.Write($"[PatchOnSelected] {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(SelectionButton), nameof(SelectionButton.OnClick), MethodType.Normal)]
    class PatchOnClick
    {
        static readonly HashSet<string> PreviewElements = new()
        {
            "CardPict", "CardClone", "CreateButton", "ImageCard",
            "NextButton", "PrevButton", "Related Cards", "ThumbButton",
            "SlotTemplate(Clone)", "Locator", "GoldpassRewardButton",
            "NormalpassRewardButton", "ButtonDuelPass"
        };

        // Button text → Menu mapping for click-based menu detection
        private static readonly Dictionary<string, Menu> TextToMenu = new()
        {
            { "DUEL", Menu.Duel },
            { "DECK", Menu.Deck },
            { "SOLO", Menu.Solo },
            { "SHOP", Menu.Shop },
            { "MISSION", Menu.Missions },
            { "Notifications", Menu.Notifications },
            { "Game Settings", Menu.Settings },
            { "Duel Pass", Menu.DuelPass }
        };

        [HarmonyPostfix]
        static void Postfix(SelectionButton __instance)
        {
            try
            {
                string btnText = TextExtractor.ExtractFirst(__instance.gameObject);
                if (btnText != null && TextToMenu.TryGetValue(btnText, out Menu menu))
                    NavigationState.CurrentMenu = menu;
            }
            catch (Exception ex) { Log.Write($"[OnClick] {ex.Message}"); }

            // Duel surrender
            if (__instance.name == "ButtonDecidePositive(Clone)" && NavigationState.IsInDuel)
            {
                NavigationState.IsInDuel = false;
                DuelState.Clear();
            }

            // Preview card info on click — delay to let UI populate
            if (PreviewElements.Contains(__instance.name))
            {
                float delay = NavigationState.CurrentMenu == Menu.DuelPass ? 1.5f : 0.5f;
                BlindDuelCore.Instance.Invoke(nameof(BlindDuelCore.ReadCardDelayed), delay);
            }

        }
    }

    [HarmonyPatch(typeof(SelectionButton), nameof(SelectionButton.OnDeselected), MethodType.Normal)]
    class PatchOnDeselected
    {
        [HarmonyPostfix]
        static void Postfix(SelectionButton __instance)
        {
            Speech.ResetDedup();
        }
    }
}
