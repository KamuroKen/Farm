using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmRoadEditor
    {
        static FarmRoadEditor() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Library/FarmRoad.request")) return;
            string command = File.ReadAllText("Library/FarmRoad.request").Trim();
            File.Delete("Library/FarmRoad.request");
            try { if (command == "inspect") Inspect(); else if (command == "apply") Apply(); }
            catch (Exception e) { File.WriteAllText("BuildArtifacts/road-error.txt", e.ToString()); Debug.LogException(e); }
        }
        [Serializable] private class RoadPlan { public RoadTile[] tiles; }
        [Serializable] private class RoadTile { public string sprite; public int x, y; }

        [MenuItem("Tools/Farm/Apply Reference Roads")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before editing roads.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity", OpenSceneMode.Additive);
            var root = scene.GetRootGameObjects().First(g => g.name == "Farm Environment");
            var map = root.transform.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
            var plan = JsonUtility.FromJson<RoadPlan>(File.ReadAllText("Assets/Farm/Design/road-layout.json"));
            var positions = plan.tiles.Select(t => new Vector3Int(t.x,t.y,0)).ToArray();
            var tiles = plan.tiles.Select(t => AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Farm/Tiles/"+t.sprite+".asset")).ToArray();
            if (tiles.Any(t => t == null)) throw new InvalidOperationException("Missing road tile asset.");
            Directory.CreateDirectory("Assets/Scenes/Backups");
            var backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeRoads.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up scene.");
            Undo.RegisterCompleteObjectUndo(map, "Redraw reference roads");
            map.ClearAllTiles();
            map.SetTiles(positions, tiles);
            map.CompressBounds();
            int moved = 0;
            foreach (string group in new[] { "Barnyard/Supplies", "Common/Yard", "Common/Wayfinding" })
            {
                var parent = root.transform.Find(group);
                if (parent == null) continue;
                foreach (var renderer in parent.GetComponentsInChildren<SpriteRenderer>())
                {
                    var bounds = renderer.bounds;
                    if (!positions.Any(p => bounds.Contains(map.GetCellCenterWorld(p)))) continue;
                    float shift = group == "Barnyard/Supplies" ? 3f : -3f;
                    Undo.RecordObject(renderer.transform, "Move prop to road verge");
                    renderer.transform.position += Vector3.up * shift;
                    moved++;
                }
            }
            int cleared = 0;
            // Clear only small ground-cover sprites directly on the new walking surface.
            var cover = root.transform.Find("Nature/Ground cover");
            if (cover != null)
                foreach (var renderer in cover.GetComponentsInChildren<SpriteRenderer>())
                    if (map.HasTile(map.WorldToCell(renderer.bounds.center)))
                    { Undo.RecordObject(renderer.gameObject, "Clear road grass"); renderer.gameObject.SetActive(false); cleared++; }
            if (map.GetUsedTilesCount() == 0 || positions.Any(p => !map.HasTile(p) || map.GetSprite(p) == null))
                throw new InvalidOperationException("Road tiles failed validation.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save road scene.");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("BuildArtifacts");
            File.WriteAllText("BuildArtifacts/road-validation.txt", "Saved " + scene.path + "\nBackup: " + backup + "\nRoad quarter tiles: " + positions.Length + "\nGround cover cleared: " + cleared + "\nProps moved to verge: " + moved + "\nAll road sprites resolved.\n");
            SceneManager.SetActiveScene(scene);
            ValidateWalkways(map);
            SceneView.RepaintAll();
            EditorApplication.delayCall += Capture;
        }
        static void ValidateWalkways(Tilemap map)
        {
            Physics2D.SyncTransforms();
            var clear = new System.Collections.Generic.HashSet<Vector3Int>();
            foreach (var p in map.cellBounds.allPositionsWithin)
            {
                if (!map.HasTile(p)) continue;
                var point = map.GetCellCenterWorld(p);
                var hits = Physics2D.OverlapCircleAll(point, .22f);
                if (!hits.Any(h => !h.isTrigger && h.GetComponentInParent<FarmPlayerController>() == null)) clear.Add(p);
            }
            var start = map.WorldToCell(new Vector3(-1f,-20f,0));
            var seen = new System.Collections.Generic.HashSet<Vector3Int>();
            var pending = new System.Collections.Generic.Queue<Vector3Int>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                if (!clear.Contains(p) || !seen.Add(p)) continue;
                foreach (var d in new[] { Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down }) pending.Enqueue(p+d);
            }
            var targets = new[] { new Vector3(-9,7,0), new Vector3(26,6.5f,0), new Vector3(-1,10,0), new Vector3(-7,-3,0), new Vector3(8.5f,-6,0) };
            foreach (var target in targets)
            {
                bool reached = seen.Any(p => Vector3.Distance(map.GetCellCenterWorld(p), target) < 1f);
                if (!reached) throw new InvalidOperationException("Road approach blocked near " + target);
            }
            File.AppendAllText("BuildArtifacts/road-validation.txt", "PASS: all five road approaches reachable with radius 0.22 against scene colliders.\n");
        }
        public static void Inspect()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity");
            Directory.CreateDirectory("BuildArtifacts");
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
            File.WriteAllLines("BuildArtifacts/road-scene.txt", transforms.Select(t => AnimationUtility.CalculateTransformPath(t, null) + " @ " + t.position + (t.GetComponent<SpriteRenderer>() != null ? " sprite=" + t.GetComponent<SpriteRenderer>().sprite?.name + " bounds=" + t.GetComponent<SpriteRenderer>().bounds : "")));
            EditorApplication.delayCall += Capture;
        }
        public static void Capture()
        {
            var go = new GameObject("Road preview camera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 24;
            camera.transform.position = new Vector3(0,0,-10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(63,83,56,255);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var target = new RenderTexture(1536,1152,24);
            target.Create();
            var previous = RenderTexture.active;
            try {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                var image = new Texture2D(1536,1152,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,1536,1152),0,0); image.Apply();
                File.WriteAllBytes("BuildArtifacts/FarmLevel-Roads.png",image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            } finally { RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
