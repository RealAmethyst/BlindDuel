using System;
using UnityEngine;
using Il2CppYgomSystem.UI;

namespace BlindDuel
{
    /// <summary>
    /// Name-based transform search utilities.
    /// Prefer these over magic child indices for resilience to UI hierarchy changes.
    /// </summary>
    public static class TransformSearch
    {
        /// <summary>
        /// Safe child access by index. Returns null (with a log) instead of throwing.
        /// Use when index-based access is unavoidable.
        /// </summary>
        public static Transform GetChild(Transform parent, int index, string context = null)
        {
            if (parent == null || index < 0 || index >= parent.childCount)
            {
                Log.Write($"[GetChild] {context ?? "unknown"}: index {index}, parent {parent?.name ?? "null"} has {parent?.childCount ?? 0} children");
                return null;
            }
            return parent.GetChild(index);
        }

        /// <summary>
        /// Find a child transform by name at any depth.
        /// More resilient than index-based access.
        /// </summary>
        public static Transform FindByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;

            // Transform.Find supports "/" paths for nested lookups
            var found = root.Find(name);
            if (found != null) return found;

            // Recursive search if path-based lookup fails
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;

                var result = FindByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Find the first active child with a specific component.
        /// </summary>
        public static T FindComponent<T>(Transform root) where T : Component
        {
            if (root == null) return null;
            return root.GetComponentInChildren<T>();
        }

        /// <summary>
        /// Walk up from <paramref name="start"/> for up to <paramref name="maxLevels"/> ancestor
        /// levels (inclusive of <paramref name="start"/> itself), invoking <paramref name="resolve"/>
        /// at each level. Returns the first level where resolve reports a hit.
        /// Replaces the copy-pasted "walk up N levels trying a resolver" loops scattered
        /// across the room/profile handlers.
        /// </summary>
        public static bool TryResolveByAncestor<T>(
            Transform start, int maxLevels,
            Func<GameObject, (bool found, T value)> resolve,
            out T result)
        {
            Transform t = start;
            for (int i = 0; i < maxLevels && t != null; i++)
            {
                var (found, value) = resolve(t.gameObject);
                if (found)
                {
                    result = value;
                    return true;
                }
                t = t.parent;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// Walk up parent levels from a button looking for the first ancestor level whose
        /// active children include more than one SelectionButton (searched inclusive of
        /// inactive descendants) — i.e. the level the button should be indexed within.
        /// Returns (index, total) at that level, or (0, 0) if no such level exists.
        /// </summary>
        public static (int index, int total) GetButtonIndexAmongAncestors(SelectionButton button)
        {
            var current = button.transform.parent;
            while (current != null)
            {
                int idx = 0, total = 0;
                for (int i = 0; i < current.childCount; i++)
                {
                    var child = current.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;
                    var sb = child.GetComponentInChildren<SelectionButton>(true);
                    if (sb == null) continue;
                    total++;
                    if (sb == button) idx = total;
                }
                if (total > 1 && idx > 0) return (idx, total);

                current = current.parent;
            }
            return (0, 0);
        }

        /// <summary>
        /// Get the index and total count of a SelectionButton among its active siblings.
        /// Returns (index, total). Returns (0, 0) if not found or only one button.
        /// </summary>
        public static (int index, int total) GetButtonIndex(SelectionButton button)
        {
            try
            {
                Transform parent = button.transform.parent;
                if (parent == null) return (0, 0);

                int index = 0;
                int total = 0;
                bool found = false;

                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (!child.gameObject.activeInHierarchy) continue;
                    if (child.GetComponent<SelectionButton>() == null) continue;

                    total++;
                    if (child.gameObject == button.gameObject)
                    {
                        index = total;
                        found = true;
                    }
                }

                if (found) return (index, total);
            }
            catch (Exception ex) { Log.Write($"[GetButtonIndex] {ex.Message}"); }
            return (0, 0);
        }
    }
}
