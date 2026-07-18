using Il2CppYgomGame.CardBrowser;
using Il2CppYgomSystem.UI;

namespace BlindDuel
{
    /// <summary>
    /// Tracks card browser state (ViewController + SnapContentManager for paging).
    /// </summary>
    public static class CardBrowserState
    {
        public static CardBrowserViewController ViewController { get; set; }
        public static SnapContentManager SnapContentManager { get; set; }

        /// <summary>
        /// True only while the browser is genuinely live on-screen. The stored refs are
        /// set in PatchBrowserStart and never explicitly cleared, so a plain non-null check
        /// would keep reporting "open" after the browser closed — causing stale card reads
        /// elsewhere (CardReader.ReadCurrentCard checks this first). Verify the underlying
        /// Il2Cpp objects are still alive and active; clear the refs on any liveness failure.
        /// </summary>
        public static bool IsOpen
        {
            get
            {
                var scm = SnapContentManager;
                if (scm == null) return false;

                try
                {
                    var vc = ViewController;
                    // A destroyed/collected Il2Cpp object either compares null, has a null
                    // gameObject, or throws when touched. Any of those means "not open".
                    if (vc == null || vc.gameObject == null || scm.gameObject == null)
                    {
                        ClearRefs();
                        return false;
                    }
                    // Inactive but not destroyed: report closed so card reads fall through
                    // to the non-browser path, but keep the refs — the browser may just be
                    // hidden behind a modal and can become active again.
                    if (!vc.gameObject.activeInHierarchy)
                        return false;
                    return true;
                }
                catch
                {
                    // Touching a collected Il2Cpp object throws — treat as closed.
                    ClearRefs();
                    return false;
                }
            }
        }

        private static void ClearRefs()
        {
            ViewController = null;
            SnapContentManager = null;
        }
        public static int CurrentPage => SnapContentManager?.currentPage ?? 0;

        /// <summary>
        /// Get the card MRK (ID) for the currently displayed page.
        /// Uses the ViewController's CardContext list indexed by currentPage.
        /// </summary>
        public static int GetCurrentMrk()
        {
            var contexts = ViewController?.m_CardContexts;
            int page = CurrentPage;
            if (contexts != null && page >= 0 && page < contexts.Count)
                return contexts[page].mrk;
            return 0;
        }
    }
}
