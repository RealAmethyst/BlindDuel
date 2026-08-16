using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppYgomGame.Duel;

namespace BlindDuel
{
    public class BlindDuelCore : MonoBehaviour
    {
        public static BlindDuelCore Instance { get; private set; }

        // Current preview data for card/item reading
        public static PreviewData Preview { get; } = new();


        public void Awake()
        {
            Instance = this;
            Log.Init();
            ScreenReader.Initialize();
            SDLController.Initialize();
            HandlerRegistry.Init();
        }

        public void OnApplicationQuit()
        {
            SDLController.Shutdown();
            ScreenReader.Shutdown();
        }

        public void Update()
        {
            // Poll SDL3 controller state
            SDLController.Update();

            // Duel hotkeys
            if (NavigationState.IsInDuel)
            {
                // Card detail line-by-line navigation (Ctrl+Up/Down or Right Stick Up/Down)
                // Index starts at 0 (Name, already spoken). Down goes to 1+.
                // Up from 0 or Down past last line = silence.
                bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool detailDown = ctrlHeld && Input.GetKeyDown(KeyCode.DownArrow);
                bool detailUp = ctrlHeld && Input.GetKeyDown(KeyCode.UpArrow);

                // Right stick on controller via SDL3
                if (!detailDown && !detailUp)
                {
                    if (SDLController.RightStickDownTriggered) detailDown = true;
                    else if (SDLController.RightStickUpTriggered) detailUp = true;
                }

                if (detailDown)
                {
                    var lines = DuelState.CardDetailLines;
                    if (lines != null)
                    {
                        int idx = DuelState.CardDetailIndex + 1;
                        if (idx < lines.Count)
                        {
                            DuelState.CardDetailIndex = idx;
                            Speech.SayImmediate(lines[idx]);
                        }
                    }
                }
                else if (detailUp)
                {
                    var lines = DuelState.CardDetailLines;
                    if (lines != null)
                    {
                        int idx = DuelState.CardDetailIndex - 1;
                        if (idx >= 0)
                        {
                            DuelState.CardDetailIndex = idx;
                            Speech.SayImmediate(lines[idx]);
                        }
                    }
                }


                if (Input.GetKeyDown(KeyCode.LeftAlt))
                {
                    Preview.Clear();
                    var cardInfo = FindObjectOfType<CardInfo>();
                    if (cardInfo != null)
                    {
                        if (!cardInfo.gameObject.activeInHierarchy)
                            cardInfo.gameObject.SetActive(true);
                        // TODO: Read card info via CardData extraction
                    }
                }

                // Field navigation hotkeys
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                DuelFieldNav.HandleInput(shift);
            }

            // P key: read crafting points (deck editor) or gems (shop)
            if (Input.GetKeyDown(KeyCode.P) &&
                (NavigationState.CurrentMenu == Menu.Deck || NavigationState.CurrentMenu == Menu.Shop))
            {
                try
                {
                    if (NavigationState.CurrentMenu == Menu.Shop)
                    {
                        int gems = Il2CppYgomGame.Utility.ItemUtil.GetHasTotalGem();
                        Speech.SayImmediate($"Gems: {gems}");
                    }
                    else
                    {
                        // CP balance per rarity. IDs 1-2 are Free/Paid Gem, IDs 100000/200000
                        // are Shine/Royal foils, so CP items start at 3: 3=N, 4=R, 5=SR, 6=UR.
                        int cpN = Il2CppYgomGame.Utility.ItemUtil.GetHasItemQuantity(3);
                        int cpR = Il2CppYgomGame.Utility.ItemUtil.GetHasItemQuantity(4);
                        int cpSR = Il2CppYgomGame.Utility.ItemUtil.GetHasItemQuantity(5);
                        int cpUR = Il2CppYgomGame.Utility.ItemUtil.GetHasItemQuantity(6);
                        Speech.SayImmediate(
                            $"Normal: {cpN}, Rare: {cpR}, Super Rare: {cpSR}, Ultra Rare: {cpUR}");
                    }
                }
                catch { Speech.SayImmediate("Points unavailable"); }
            }

            // Duel log selection tracking
            if (DuelState.IsDuelLogOpen)
                DuelLogReader.PollSelection();

            // Speak deferred button text if no dialog consumed it within the same frame.
            // This mirrors the menu system's deferred button processing in Poll().
            if (!string.IsNullOrEmpty(DuelState.LastQueuedButtonText)
                && UnityEngine.Time.frameCount > DuelState.LastQueuedButtonFrame)
            {
                if (DuelState.LastQueuedButtonInterrupt)
                    Speech.SayItem(DuelState.LastQueuedButtonText);
                else
                    Speech.SayQueued(DuelState.LastQueuedButtonText);
                DuelState.LastQueuedButtonText = null;
            }

            // Detection: screen/dialog changes
            DialogDetector.Poll();
            ScreenDetector.Poll();

            // Speak the current value of any open InputDigit (+/-) dialog as it changes.
            InputDigitWatcher.Poll();

            // Settings slider / toggle value-change announcement
            SettingsHandler.PollSettingValue();

            // Re-announce friend-selection count when it changes on the RoomInvite screen.
            RoomInviteHandler.Poll();

            // Re-announce member / spectator counts + seat occupancy in the Duel Room.
            RoomHandler.Poll();

            // Re-announce ON/OFF toggle and other in-row setting flips on Create Room.
            RoomCreateHandler.Poll();
        }

        /// <summary>
        /// Invokable card reading method — used with MonoBehaviour.Invoke() for delayed reads.
        /// Only used for selection list cards during duels (field/hand/zone reads go
        /// through InvokeFocusField instead). Also used outside duels for deck editor etc.
        /// </summary>
        public void ReadCardDelayed()
        {
            bool useQueued = PatchCardSelectionListSetTitle.ConsumeQueuedFlag();
            CardReader.ReadAndSpeak(suffix: null, queued: useQueued);
        }

        /// <summary>
        /// Invokable method for reading item preview popups (duel pass rewards, shop items).
        /// Called via delayed Invoke() from the ItemPreviewViewController patch.
        /// </summary>
        public void ReadItemPreviewDelayed()
        {
            CardReader.ReadPreviewAndSpeak();
        }


        /// <summary>
        /// Invokable method for reading MDMarkup article content (notifications, news).
        /// Called via debounced Invoke() from the MDMarkupBoardContainerWidget.OnStart patch.
        /// Reads from the focused VC's hierarchy after content has settled.
        /// </summary>
        public void ReadMDMarkupContent()
        {
            try
            {
                var focusVC = ScreenDetector.GetFocusVC();
                if (focusVC == null)
                {
                    Log.Write("[MDMarkup] No focused VC");
                    return;
                }

                var texts = TextExtractor.ExtractAll(focusVC.gameObject, new TextSearchOptions
                {
                    ActiveOnly = true,
                    FilterBanned = false,
                    ExcludePathContaining = new List<string> { "SnapContent" },
                    LogPrefix = "[MDMarkup]"
                });

                if (texts.Count == 0)
                {
                    Log.Write("[MDMarkup] No text found in focused VC");
                    return;
                }

                var parts = new List<string>();
                foreach (var r in texts)
                    parts.Add(r.Text);

                string announcement = string.Join(". ", parts);
                Log.Write($"[MDMarkup] {announcement}");
                Speech.AnnounceScreen(announcement);
            }
            catch (Exception ex) { Log.Write($"[MDMarkup] Error: {ex.Message}"); }
        }

    }
}
