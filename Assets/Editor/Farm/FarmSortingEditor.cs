using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    public static class FarmSortingEditor
    {
        private const string RulesPath = "Assets/Farm/Design/sorting-rules.json";
        [Serializable] private class Category { public string name; public int order; public string[] sprites; }
        [Serializable] private class LayerRule { public string name; public int order; }
        [Serializable] private class Rules
        {
            public int playerOrder, foregroundStart;
            public Category[] categories;
            public LayerRule[] tileLayers;
        }
        [Serializable] private class Entry
        {
            public string path, sprite, category;
            public int before, after;
        }
        [Serializable] private class Report
        {
            public bool success, foregroundOrderPreserved, idempotent;
            public int foregroundCount, foregroundMin, foregroundMax;
            public string backup, completedUtc;
            public Entry[] objects;
        }
        private class Foreground
        {
            public Component component;
            public int sourceOrder, previousOrder;
            public Entry entry;
            public int Order
            {
                get => component is SortingGroup group ? group.sortingOrder : ((SpriteRenderer)component).sortingOrder;
                set
                {
                    if (component is SortingGroup group) { group.sortingLayerID = 0; group.sortingOrder = value; }
                    else { var renderer = (SpriteRenderer)component; renderer.sortingLayerID = 0; renderer.sortingOrder = value; }
                }
            }
        }

        [MenuItem("Tools/Farm/Normalize Sorting Orders")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before normalizing sorting.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity", OpenSceneMode.Additive);
            Directory.CreateDirectory("Assets/Scenes/Backups");
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeSorting.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up the live scene.");
            var report = NormalizeInternal(scene);
            report.backup = backup;
            var first = report.objects.Select(e => e.path + ":" + e.after).ToArray();
            var repeated = NormalizeInternal(scene);
            report.idempotent = first.SequenceEqual(repeated.objects.Select(e => e.path + ":" + e.after));
            if (!report.idempotent) throw new InvalidOperationException("Sorting normalization is not idempotent.");
            report.success = true;
            report.completedUtc = DateTime.UtcNow.ToString("o");
            string prefabPath = "Assets/Farm/Player/BaseCharacterPlayer.prefab";
            if (File.Exists(prefabPath))
            {
                var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var renderer = prefab.GetComponent<SpriteRenderer>();
                    renderer.sortingLayerID = 0;
                    renderer.sortingOrder = LoadRules().playerOrder;
                    PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save normalized scene.");
            AssetDatabase.SaveAssets();
            SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory("BuildArtifacts");
            File.WriteAllText("BuildArtifacts/farm-sorting-report.json", JsonUtility.ToJson(report, true));
            SceneView.RepaintAll();
            Debug.Log($"Farm sorting normalized: player 100, ground props below 100, {report.foregroundCount} foreground objects at {report.foregroundMin}–{report.foregroundMax}; composition preserved.");
        }

        public static void Normalize(Scene scene) => NormalizeInternal(scene);

        public static void SetSourceDepth(GameObject go, int depth)
        {
            var source = go.GetComponent<FarmSortingDepth>();
            if (source == null) source = go.AddComponent<FarmSortingDepth>();
            source.SourceOrder = depth;
            EditorUtility.SetDirty(source);
        }

        private static Rules LoadRules() => JsonUtility.FromJson<Rules>(File.ReadAllText(RulesPath));

        private static string PathOf(Transform transform)
        {
            return transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
        }

        private static Report NormalizeInternal(Scene scene)
        {
            var rules = LoadRules();
            var categories = rules.categories.SelectMany(c => c.sprites.Select(s => new { name = s, category = c }))
                .ToDictionary(x => x.name, x => x.category, StringComparer.OrdinalIgnoreCase);
            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<SpriteRenderer>(true)).ToArray();
            var groups = roots.SelectMany(r => r.GetComponentsInChildren<SortingGroup>(true))
                .Where(g => g.transform.parent == null || g.transform.parent.GetComponentInParent<SortingGroup>(true) == null).ToArray();
            var standalone = renderers.Where(r => r.GetComponentInParent<SortingGroup>(true) == null).ToArray();
            var unknown = standalone.Where(r => r.sprite != null && r.GetComponentInParent<FarmPlayerController>(true) == null
                && !categories.ContainsKey(r.sprite.name)).Select(r => PathOf(r.transform) + " => " + r.sprite.name).ToArray();
            if (unknown.Length > 0)
            {
                Directory.CreateDirectory("BuildArtifacts");
                File.WriteAllLines("BuildArtifacts/sorting-unclassified.txt", unknown);
                throw new InvalidOperationException("Unclassified sprites; see BuildArtifacts/sorting-unclassified.txt. No sorting values changed.");
            }
            var tilemaps = roots.SelectMany(r => r.GetComponentsInChildren<TilemapRenderer>(true)).ToArray();
            var tileOrders = rules.tileLayers.ToDictionary(l => l.name, l => l.order);
            foreach (var map in tilemaps)
                if (map.transform.parent == null || !tileOrders.ContainsKey(map.transform.parent.name))
                    throw new InvalidOperationException("Unclassified tilemap: " + PathOf(map.transform));

            var entries = new List<Entry>();
            var foreground = new List<Foreground>();
            foreach (var group in groups)
            {
                var entry = new Entry { path = PathOf(group.transform), sprite = "(Sorting Group)", category = "Foreground group", before = group.sortingOrder };
                entries.Add(entry);
                foreground.Add(new Foreground { component = group, previousOrder = group.sortingOrder,
                    sourceOrder = group.GetComponent<FarmSortingDepth>()?.SourceOrder ?? group.sortingOrder, entry = entry });
            }
            foreach (var renderer in standalone)
            {
                if (renderer.sprite == null) continue;
                bool player = renderer.GetComponentInParent<FarmPlayerController>(true) != null;
                var category = player ? null : categories[renderer.sprite.name];
                var entry = new Entry { path = PathOf(renderer.transform), sprite = renderer.sprite.name,
                    category = player ? "Player" : category.name, before = renderer.sortingOrder };
                entries.Add(entry);
                if (!player && category.order >= rules.foregroundStart)
                    foreground.Add(new Foreground { component = renderer, previousOrder = renderer.sortingOrder,
                        sourceOrder = renderer.GetComponent<FarmSortingDepth>()?.SourceOrder ?? renderer.sortingOrder, entry = entry });
                else
                {
                    Undo.RecordObject(renderer, "Normalize farm sorting");
                    renderer.sortingLayerID = 0;
                    renderer.sortingOrder = player ? rules.playerOrder : category.order;
                    entry.after = renderer.sortingOrder;
                    EditorUtility.SetDirty(renderer);
                }
            }
            var ranks = foreground.Select(f => f.sourceOrder).Distinct().OrderBy(n => n)
                .Select((value, rank) => new { value, order = rules.foregroundStart + rank }).ToDictionary(x => x.value, x => x.order);
            foreach (var item in foreground)
            {
                Undo.RecordObject(item.component, "Normalize foreground sorting");
                SetSourceDepth(item.component.gameObject, item.sourceOrder);
                item.Order = ranks[item.sourceOrder];
                item.entry.after = item.Order;
                EditorUtility.SetDirty(item.component);
            }
            // Dense ranking must preserve every original <, > and == relationship.
            foreach (var a in foreground)
            foreach (var b in foreground)
                if (Math.Sign(a.sourceOrder.CompareTo(b.sourceOrder)) != Math.Sign(a.Order.CompareTo(b.Order)))
                    throw new InvalidOperationException("Foreground relative order changed.");
            foreach (var map in tilemaps)
            {
                var entry = new Entry { path = PathOf(map.transform), sprite = "(Tilemap)", category = "Terrain", before = map.sortingOrder };
                Undo.RecordObject(map, "Normalize terrain sorting");
                map.sortingLayerID = 0;
                map.sortingOrder = tileOrders[map.transform.parent.name];
                entry.after = map.sortingOrder;
                entries.Add(entry);
                EditorUtility.SetDirty(map);
            }
            return new Report { objects = entries.ToArray(), foregroundOrderPreserved = true, foregroundCount = foreground.Count,
                foregroundMin = foreground.Count == 0 ? rules.foregroundStart : foreground.Min(f => f.Order),
                foregroundMax = foreground.Count == 0 ? rules.foregroundStart : foreground.Max(f => f.Order) };
        }
    }
}
