using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppYgomGame.Menu;
using Il2CppYgomGame.TextIDs;
using Il2CppYgomSystem.Utility;
using UnityEngine;

namespace BlindDuel
{
    /// <summary>
    /// Reads all profile stats after the async API data arrives.
    /// Rendered items are read from the UI; off-screen items use TextData + dataInfos directly.
    /// </summary>
    [HarmonyPatch(typeof(ProfileDataViewController), nameof(ProfileDataViewController.UpdateProfileData))]
    class PatchProfileDataReady
    {
        [HarmonyPostfix]
        static void Postfix(ProfileDataViewController __instance)
        {
            try
            {
                var scrollView = __instance.isvData;
                if (scrollView == null) return;

                int total = scrollView.dataCount;
                if (total == 0) return;

                int dataStart = __instance.dataInfoHeadIndex;
                var dataInfos = __instance.dataInfos;
                var parts = new List<string>();
                var readDataIndices = new HashSet<int>();

                // Pass 1: read rendered items from UI
                for (int i = 0; i < total; i++)
                {
                    var entity = scrollView.GetEntityByDataIndex(i);
                    if (entity == null) continue;

                    var texts = TextExtractor.ExtractAll(entity, new TextSearchOptions
                    {
                        ActiveOnly = true,
                        FilterBanned = false
                    });

                    if (texts.Count >= 2)
                        parts.Add($"{texts[0].Text}: {texts[1].Text}");
                    else if (texts.Count == 1)
                        parts.Add(texts[0].Text);

                    // Track which dataInfos indices we've already read
                    if (i >= dataStart)
                        readDataIndices.Add(i - dataStart);
                }

                // Pass 2: read remaining data entries directly via TextData
                if (dataInfos != null)
                {
                    for (int d = 0; d < dataInfos.Count; d++)
                    {
                        if (readDataIndices.Contains(d)) continue;

                        try
                        {
                            var info = dataInfos[d];
                            string title = TextData.GetText(info.record);
                            parts.Add($"{title}: {info.value}");
                        }
                        catch (Exception ex)
                        {
                            Log.Write($"[ProfileData] TextData error at {d}: {ex.Message}");
                            continue; // skip bad entry, keep reading remaining stats
                        }
                    }
                }

                if (parts.Count > 0)
                    Speech.SayQueued(string.Join(". ", parts));

                Log.Write($"[ProfileData] Read {parts.Count} items (dataInfos={dataInfos?.Count ?? 0})");
            }
            catch (Exception ex)
            {
                Log.Write($"[ProfileData] error: {ex.Message}");
            }
        }
    }
}
