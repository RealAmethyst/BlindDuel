using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppTMPro;
using Il2CppYgomSystem.UI;
using Il2CppYgomSystem.ElementSystem;
using Il2CppYgomGame.Menu;
using Il2CppYgomGame.Enquete;

namespace BlindDuel
{
    /// <summary>
    /// Detects ViewController changes, announces screen headers,
    /// and queues the currently focused button text.
    /// Defers announcements until the screen is fully loaded (isLoading == false).
    /// </summary>
    public static class ScreenDetector
    {
        // VC name → Menu mapping for automatic context detection
        private static readonly Dictionary<string, Menu> VCToMenu = new(StringComparer.OrdinalIgnoreCase)
        {
            { "SoloMode", Menu.Solo },
            { "SoloGate", Menu.Solo },
            { "SoloSelectChapter", Menu.Solo },
            { "DuelClient", Menu.Duel },
            { "DuelLive", Menu.Duel },
            { "DeckEdit", Menu.Deck },
            { "DeckBrowser", Menu.Deck },
            { "DeckSelect", Menu.Deck },
            { "Shop", Menu.Shop },
            { "SettingMenuViewController", Menu.Settings },
        };

        // Async polling state
        private static bool _pendingEnqueteCheck;
        private static string _lastEnquetePage = "";
        private static DownloadViewController _activeDownloadVC;
        private static int _lastDownloadPercent = -1;

        // Deferred screen announcement — wait for screen to finish loading
        private static ViewController _pendingVC;
        private static string _pendingCleanName;

        // Button selected during pending screen — captured from OnSelected patch,
        // used by QueueFocusedItem after screen announcement
        private static SelectionButton _deferredButton;

        /// <summary>
        /// True while a screen change has been detected but not yet announced
        /// (waiting for IsReadyTransition + !isLoading). Button patches should
        /// suppress speech during this window to avoid speaking items before the header.
        /// </summary>
        internal static bool HasPendingScreen => _pendingVC != null;

        /// <summary>
        /// Called each frame. Checks for screen changes, polls async screens.
        /// </summary>
        public static void Poll()
        {
            CheckScreenChange();
            CheckPendingAnnouncement();
            CheckEnqueteScreen();
            CheckDownloadProgress();
        }

        /// <summary>
        /// Register a download VC for progress polling.
        /// Called from DialogPatches when DownloadViewController.OnCreatedView fires.
        /// </summary>
        public static void TrackDownload(DownloadViewController vc)
        {
            _activeDownloadVC = vc;
            _lastDownloadPercent = -1;
        }

        /// <summary>
        /// Direct game-native check: are all screen transitions complete?
        /// More reliable than HasPendingScreen for real-time checks in patches,
        /// since it doesn't depend on our Poll() timing.
        /// </summary>
        internal static bool IsScreenReady()
        {
            try
            {
                var contentManager = GameObject.Find("UI/ContentCanvas/ContentManager");
                if (contentManager == null) return true;
                var vcm = contentManager.GetComponent<ViewControllerManager>();
                return vcm == null || vcm.IsReadyTransition();
            }
            catch (Exception ex) { Log.Write($"[IsScreenReady] {ex.Message}"); return true; }
        }

        internal static ViewController GetFocusVC()
        {
            var contentManager = GameObject.Find("UI/ContentCanvas/ContentManager");
            if (contentManager == null) return null;
            var vcm = contentManager.GetComponent<ViewControllerManager>();
            return vcm?.GetFocusViewController();
        }

        /// <summary>
        /// Generic convenience accessor: fetch the current focus ViewController and
        /// cast it to a specific subclass in one call.
        /// </summary>
        internal static T GetFocusVC<T>() where T : ViewController => GetFocusVC()?.TryCast<T>();

        internal static ElementObjectManager GetView(ViewController vc)
        {
            try
            {
                var baseVC = vc.TryCast<BaseMenuViewController>();
                if (baseVC == null)
                {
                    Log.Write($"[GetView] TryCast failed for: {vc.name} ({vc.GetIl2CppType().Name})");
                    return null;
                }
                return baseVC.m_View;
            }
            catch (Exception ex)
            {
                Log.Write($"[GetView] Error for {vc.name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find screen title from a ViewController using EOM elements, then fallback scans.
        /// </summary>
        internal static string FindScreenTitle(ViewController vc)
        {
            if (vc == null) return null;

            var eom = GetView(vc);

            // Try EOM serializedElements first (most reliable)
            List<(string label, string text)> elements = null;
            if (eom != null)
            {
                elements = ElementReader.ReadElements(eom, $"[FindTitle] {vc.name} |");

                // Fallback: scan all TMP in EOM hierarchy
                if (elements.Count == 0)
                {
                    var textResults = TextExtractor.ExtractAll(eom.gameObject, new TextSearchOptions
                    {
                        ActiveOnly = false,
                        FilterBanned = false,
                        LogPrefix = $"[FindTitle-fallback] {vc.name} |"
                    });
                    elements = new List<(string, string)>();
                    foreach (var r in textResults)
                        elements.Add((r.Path, r.Text));
                }
            }

            // Last resort: scan VC's own hierarchy
            if (elements == null || elements.Count == 0)
            {
                var textResults = TextExtractor.ExtractAll(vc.gameObject, new TextSearchOptions
                {
                    ActiveOnly = false,
                    FilterBanned = false,
                    LogPrefix = $"[FindTitle-vc] {vc.name} |"
                });
                elements = new List<(string, string)>();
                foreach (var r in textResults)
                    elements.Add((r.Path, r.Text));
            }

            if (elements.Count == 0) return null;

            var (title, message) = ElementReader.ExtractTitleAndBody(elements);

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                return $"{title}. {message}";
            return title ?? message;
        }

        /// <summary>
        /// Read the game's header bar text (the localized screen name).
        /// </summary>
        internal static string ReadGameHeaderText()
        {
            try
            {
                var headerVC = HeaderViewController.instance;
                if (headerVC == null || !headerVC.gameObject.activeInHierarchy) return null;

                var eom = headerVC.ui;
                if (eom == null) return null;

                string labelKey = headerVC.TXT_LABEL;
                if (string.IsNullOrEmpty(labelKey)) return null;

                // Try the labeled element first
                string text = ElementReader.GetElementText(eom, labelKey);
                if (!string.IsNullOrWhiteSpace(text) && !IsNumericOnly(text)) return text;

                // Fallback: first non-numeric active text in the header
                // (skip gem count which is always numeric)
                var results = TextExtractor.ExtractAll(eom.gameObject, new TextSearchOptions { ActiveOnly = true, FilterBanned = false });
                foreach (var r in results)
                {
                    if (!string.IsNullOrWhiteSpace(r.Text) && !IsNumericOnly(r.Text))
                        return r.Text;
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Write($"[HeaderVC] Error: {ex.Message}");
                return null;
            }
        }

        private static void UpdateMenuContext(string cleanName)
        {
            if (VCToMenu.TryGetValue(cleanName, out Menu menu))
            {
                NavigationState.CurrentMenu = menu;
                Log.Write($"[MenuContext] Set to {menu} from VC: {cleanName}");
            }
            else if (NavigationState.CurrentMenu != Menu.None)
            {
                Log.Write($"[MenuContext] Reset from {NavigationState.CurrentMenu} to None for VC: {cleanName}");
                NavigationState.CurrentMenu = Menu.None;
            }
        }

        /// <summary>
        /// Detect VC changes immediately (update state, context, handler),
        /// but defer the actual announcement until the screen is loaded.
        /// </summary>
        private static void CheckScreenChange()
        {
            try
            {
                var focusVC = GetFocusVC();
                if (focusVC == null) return;

                string vcName = focusVC.name;
                if (vcName == NavigationState.LastFocusVCName) return;
                NavigationState.LastFocusVCName = vcName;

                string cleanName = vcName.EndsWith("(Clone)") ? vcName[..^7] : vcName;
                UpdateMenuContext(cleanName);
                HandlerRegistry.SetCurrentFromVC(cleanName);

                // HomeSubMenu overlay doesn't register as a focus VC, so when a pushed
                // VC (e.g. Settings) closes and focus returns to Home while the submenu
                // is still open, VC-name routing picks HomeHandler instead of HomeSubMenu.
                // Only override when routing landed on HomeHandler (don't clobber pushed VCs).
                var sub = HomeSubMenuState.CurrentInstance;
                if (HandlerRegistry.Current is HomeHandler && sub != null && !sub.WasCollected)
                {
                    HandlerRegistry.SetCurrentFromVC("HomeSubMenu");
                }

                // Setup screens and movie playback — skip announcement
                if (cleanName is "GameEntryV1" or "GameEntrySequenceV2" or "Scenario") return;

                // MDMarkup articles load async — patched via OnCreatedMDMarkupAsset
                if (cleanName == "MDMarkupAsset") return;

                // Enquete loads async — poll in Update instead
                if (cleanName == "Enquete")
                {
                    _pendingEnqueteCheck = true;
                    _lastEnquetePage = "";
                    return;
                }

                // Defer announcement until screen is ready
                _pendingVC = focusVC;
                _pendingCleanName = cleanName;
                _deferredButton = null;
            }
            catch (Exception ex) { Log.Write($"[CheckScreenChange] {ex.Message}"); }
        }

        /// <summary>
        /// Poll the pending screen until transitions are done and data is loaded, then announce.
        /// </summary>
        private static void CheckPendingAnnouncement()
        {
            if (_pendingVC == null) return;

            try
            {
                // Wait for all transitions to complete (enter animations, fades, etc.)
                var contentManager = GameObject.Find("UI/ContentCanvas/ContentManager");
                if (contentManager == null) return;
                var vcm = contentManager.GetComponent<ViewControllerManager>();
                if (vcm != null && !vcm.IsReadyTransition())
                    return; // Transitions still playing — check next frame

                // Also wait for async data loading to finish
                var baseVC = _pendingVC.TryCast<BaseMenuViewController>();
                if (baseVC != null && baseVC.isLoading)
                    return; // Still loading data — check next frame

                // Screen is fully ready
                var vc = _pendingVC;
                var cleanName = _pendingCleanName;
                _pendingVC = null;
                _pendingCleanName = null;

                // Let the handler announce if it wants to
                var handler = HandlerRegistry.Current;
                if (handler != null && handler.OnScreenEntered(cleanName))
                {
                    NavigationState.ScreenJustAnnounced = true;

                    // Also queue focused item so user knows where they are
                    QueueFocusedItem();
                    return;
                }

                // Standard screen: combine header + title
                string headerText = ReadGameHeaderText();
                string titleText = FindScreenTitle(vc);

                string announcement = (headerText, titleText) switch
                {
                    (not null, not null) => $"{headerText}. {titleText}",
                    (not null, _) => headerText,
                    (_, not null) => titleText,
                    _ => null
                };

                if (announcement == null)
                {
                    Log.Write($"[ScreenChange] No text found for: {cleanName}");
                    return;
                }

                Log.Write($"[ScreenChange] {cleanName} | header='{headerText}', title='{titleText}'");
                Speech.AnnounceScreen(announcement);
                NavigationState.ScreenJustAnnounced = true;

                // Queue the currently focused button so user knows where they are
                QueueFocusedItem();
            }
            catch (Exception ex) { Log.Write($"[CheckPendingAnnouncement] {ex.Message}"); }
        }

        private static void CheckEnqueteScreen()
        {
            if (!_pendingEnqueteCheck) return;

            try
            {
                var focusVC = GetFocusVC();
                if (focusVC == null) return;

                string cleanName = focusVC.name.EndsWith("(Clone)") ? focusVC.name[..^7] : focusVC.name;
                if (cleanName != "Enquete")
                {
                    _pendingEnqueteCheck = false;
                    _lastEnquetePage = "";
                    return;
                }

                var enqueteVC = focusVC.TryCast<EnqueteViewController>();
                if (enqueteVC == null) return;

                var pageTextComp = enqueteVC.m_PageText;
                string pageText = pageTextComp?.text?.Trim();
                if (string.IsNullOrEmpty(pageText)) return; // Still loading

                if (pageText == _lastEnquetePage) return;
                _lastEnquetePage = pageText;

                // Collect question text, filtering out buttons and option controls
                var collected = TextExtractor.ExtractAll(focusVC.gameObject, new TextSearchOptions
                {
                    ActiveOnly = true,
                    FilterBanned = false,
                    ExcludePathContaining = new() { "button", "shortcut", "toggle", "entity", "checkbox" }
                });

                var texts = new List<string>();
                foreach (var r in collected)
                {
                    if (r.Text != pageText)
                        texts.Add(r.Text);
                }

                if (texts.Count == 0) return;

                string announcement = string.Join(". ", texts) + $". {pageText}";
                Speech.AnnounceScreen(announcement);
                QueueFocusedItem();
            }
            catch (Exception ex) { Log.Write($"[Enquete] Error: {ex.Message}"); }
        }

        private static void CheckDownloadProgress()
        {
            try
            {
                if (_activeDownloadVC == null) return;

                if (_activeDownloadVC.WasCollected || !_activeDownloadVC.gameObject.activeInHierarchy)
                {
                    _activeDownloadVC = null;
                    _lastDownloadPercent = -1;
                    return;
                }

                var controller = _activeDownloadVC.downloadController;
                if (controller == null) return;

                int percent = (int)(controller.TotalProgress * 100f);
                if (percent == _lastDownloadPercent) return;
                _lastDownloadPercent = percent;

                if (percent >= 100)
                {
                    var stateText = _activeDownloadVC.DownloadingStateText;
                    string msg = stateText?.text?.Trim();
                    if (string.IsNullOrEmpty(msg))
                        msg = _activeDownloadVC.DownloadingText?.text?.Trim();

                    Speech.SayQueued(msg ?? $"{percent} percent");
                    _activeDownloadVC = null;
                    _lastDownloadPercent = -1;
                }
                else
                {
                    Speech.SayQueued($"{percent} percent");
                }
            }
            catch (Exception ex) { Log.Write($"[Download] {ex.Message}"); }
        }

        /// <summary>
        /// Called from PatchOnSelected when HasPendingScreen is true.
        /// Captures the button so QueueFocusedItem can use it after the screen is announced.
        /// </summary>
        internal static void DeferFocusedButton(SelectionButton btn) => _deferredButton = btn;

        /// <summary>
        /// After a screen header, queue the focused button text so the user knows where they are.
        /// Uses SelectorManager.currentItem, falling back to the deferred button captured
        /// from OnSelected during the pending screen window.
        /// </summary>
        internal static void QueueFocusedItem()
        {
            try
            {
                SelectionButton btn = null;

                var selectedItem = SelectorManager.currentItem;
                if (selectedItem != null)
                    btn = selectedItem.TryCast<SelectionButton>();

                // Fallback: button captured from OnSelected during pending screen
                if (btn == null)
                    btn = _deferredButton;
                _deferredButton = null;

                if (btn == null || btn.WasCollected || !btn.gameObject.activeInHierarchy) return;

                string btnText = null;
                var activeHandler = HandlerRegistry.Current;
                if (activeHandler != null)
                    btnText = activeHandler.OnButtonFocused(btn);

                if (string.IsNullOrWhiteSpace(btnText))
                {
                    btnText = TextExtractor.ExtractFirst(btn.gameObject);
                    if (!string.IsNullOrWhiteSpace(btnText))
                    {
                        var (index, total) = TransformSearch.GetButtonIndex(btn);
                        if (total > 1)
                            btnText += $"\n{index} of {total}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(btnText))
                    Speech.SayQueued(btnText);
            }
            catch (Exception ex) { Log.Write($"[QueueFocusedItem] {ex.Message}"); }
        }

        private static bool IsNumericOnly(string text)
        {
            foreach (char c in text.Trim())
            {
                if (c < '0' || c > '9') return false;
            }
            return text.Trim().Length > 0;
        }
    }
}
