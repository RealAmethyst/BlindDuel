using System.Collections.Generic;
using Il2CppYgomGame.Duel;
using Il2CppYgomSystem.UI;

namespace BlindDuel
{
    /// <summary>
    /// Tracks duel-specific state: cards on field, LP, etc.
    /// </summary>
    public static class DuelState
    {
        /// <summary>
        /// True between DuelEndOperation.Setup and DuelEndMessage.Setup —
        /// the duel result animation is playing and buttons should not speak yet.
        /// </summary>
        public static bool IsShowingResult { get; set; }

        /// <summary>
        /// True while the duel log viewer is open (LT/L2 to open).
        /// </summary>
        public static bool IsDuelLogOpen { get; set; }

        /// <summary>
        /// Reference to the active DuelLogScrollView while the log is open.
        /// Used to poll topitemDataindex for scroll tracking.
        /// </summary>
        public static DuelLogScrollView LogScrollView { get; set; }

        /// <summary>
        /// Reference to the DuelLogController while the log is open.
        /// Used to look up team data for log entries.
        /// </summary>
        public static DuelLogController LogController { get; set; }

        /// <summary>
        /// True once the first phase change fires (Draw Phase, etc.).
        /// Before this, the duel is still in its opening setup (dealing hands,
        /// showing match tips) and card reading should be suppressed.
        /// </summary>
        public static bool HasPhaseStarted { get; set; }

        /// <summary>
        /// True briefly after a game event message (summon, effect, phase banner, etc.)
        /// fires during a duel. The next field focus or button read consumes this flag
        /// and queues its speech after the message instead of interrupting it.
        /// </summary>
        public static bool MessageJustAnnounced { get; set; }

        /// <summary>
        /// True while a CardSelectionList is being set up (between SetList prefix
        /// and HandleTitle postfix). Buttons/field focus that fire during this
        /// window are deferred instead of spoken, mirroring HasPendingScreen
        /// for normal menus.
        /// </summary>
        public static bool HasPendingSelection { get; set; }

        /// <summary>
        /// Text of the last button that was deferred during a duel.
        /// Used by HandleTitle to re-queue after a dialog interrupts it.
        /// If not consumed by HandleTitle, Update() speaks it on the next frame.
        /// </summary>
        public static string LastQueuedButtonText { get; set; }
        public static int LastQueuedButtonFrame { get; set; }

        /// <summary>
        /// True when deferred button text should interrupt (normal navigation).
        /// False when it should queue (after a screen/dialog announcement).
        /// </summary>
        public static bool LastQueuedButtonInterrupt { get; set; }

        /// <summary>
        /// True after CardCommand closes (player selected Summon/Set/etc.).
        /// Suppresses the next field focus so the selection prompt message
        /// speaks first without a brief blip of the auto-focused zone.
        /// Consumed by the next OnFieldFocused or message handler.
        /// </summary>
        public static bool SuppressNextFieldFocus { get; set; }

        /// <summary>
        /// Card detail lines for Ctrl+Up/Down navigation.
        /// [0] = summary (already spoken), [1+] = detail lines.
        /// Null when no card is focused or detail reading is unavailable.
        /// </summary>
        public static List<string> CardDetailLines { get; set; }

        /// <summary>
        /// Current position in CardDetailLines for Ctrl+Up/Down.
        /// 0 = summary (already spoken), 1+ = detail lines.
        /// </summary>
        public static int CardDetailIndex { get; set; }

        /// <summary>
        /// Button deferred during selection setup. Queued after the title speaks.
        /// </summary>
        public static SelectionButton DeferredSelectionButton { get; set; }

        /// <summary>
        /// Field focus deferred during selection setup. Queued after the title speaks.
        /// (player, position, viewIndex) stored as a tuple, null if none deferred.
        /// </summary>
        public static (int player, int position, int viewIndex)? DeferredFieldFocus { get; set; }

        /// <summary>
        /// Player and position of the last focused pile zone (Graveyard, Extra Deck, Banished).
        /// Used by DuelHandler to look up card IDs when browsing zone lists via X button.
        /// </summary>
        public static int LastBrowsePlayer { get; set; } = -1;
        public static int LastBrowsePosition { get; set; } = -1;

        /// <summary>
        /// Logical scroll index for zone browse lists (Graveyard, Extra Deck, Banished).
        /// Adjusted by BrowseDirection on each button focus event in the browse view.
        /// Reset to -1 when a pile zone is focused (first focus will advance to 0).
        /// </summary>
        public static int BrowseIndex { get; set; } = -1;

        /// <summary>
        /// Direction of browse navigation: +1 = scrolling down, -1 = scrolling up.
        /// Set by InputPatches when UP/DOWN input is detected during duel browse.
        /// </summary>
        public static int BrowseDirection { get; set; } = 1;

        /// <summary>
        /// Last logical index that was spoken in browse mode.
        /// Used to suppress duplicate reads when clamped at list boundaries.
        /// </summary>
        public static int LastBrowseLogicalIdx { get; set; } = -1;

        public static void Clear()
        {
            IsShowingResult = false;
            IsDuelLogOpen = false;
            LogScrollView = null;
            LogController = null;
            HasPhaseStarted = false;
            MessageJustAnnounced = false;
            HasPendingSelection = false;
            SuppressNextFieldFocus = false;
            CardDetailLines = null;
            CardDetailIndex = 0;
            LastQueuedButtonText = null;
            DeferredSelectionButton = null;
            DeferredFieldFocus = null;
            LastBrowsePlayer = -1;
            LastBrowsePosition = -1;
            BrowseIndex = -1;
            BrowseDirection = 1;
            LastBrowseLogicalIdx = -1;
        }

        /// <summary>
        /// Returns the viewer's player index from the Engine.
        /// In solo/AI duels this is 0; in online duels it can be 0 or 1.
        /// Falls back to 0 if the API is unavailable.
        /// </summary>
        public static int GetMyPlayerNum()
        {
            try
            {
                var client = DuelClient.instance;
                if (client != null)
                {
                    var init = client.engineInitializer;
                    if (init != null)
                        return init.myPlayerNum;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// True when the given Engine player index is the viewer (me).
        /// </summary>
        public static bool IsMyPlayer(int player) => player == GetMyPlayerNum();

        /// <summary>
        /// True when ShowCardNameData.team refers to the opponent.
        /// The team field is player-0-relative: team=true means player 0's card.
        /// When we're player 1, that mapping is inverted.
        /// </summary>
        public static bool IsOpponentTeam(bool team)
        {
            int myPlayer = GetMyPlayerNum();
            // team=true → player 0's card. If I'm player 0, that's mine (not opponent).
            // team=true → player 0's card. If I'm player 1, that's opponent.
            return team ? (myPlayer != 0) : (myPlayer == 0);
        }
    }
}
