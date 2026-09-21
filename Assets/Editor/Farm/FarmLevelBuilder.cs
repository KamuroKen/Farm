using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    /// <summary>Editor-only authoring. The saved scene needs no generator at runtime.</summary>
    public static class FarmLevelBuilder
    {
        private const string ScenePath = "Assets/Scenes/FarmLevel.unity";
        private const string FarmPath = "Assets/Farm";
        private const string RootName = "Farm Environment";
        private const int FarmLayer = 0;

        [Serializable] private class Plan
        {
            public int width, height;
            public SpriteSpec[] sprites;
            public LayerSpec[] tileLayers;
            public GroupSpec[] groups;
            public ObjectSpec[] objects;
            public ColliderSpec[] colliders;
            public MarkerSpec[] markers;
            public CameraSpec camera;
        }
        [Serializable] private class SpriteSpec
        {
            public string name, atlas;
            public int x, y, w, h;
            public float pivotX, pivotY;
        }
        [Serializable] private class TileSpec { public string sprite; public int x, y; }
        [Serializable] private class LayerSpec
        {
            public string name;
            public float cellSize;
            public int order;
            public TileSpec[] tiles;
        }
        [Serializable] private class GroupSpec { public string name; public int sortingOrder, depthOrder; }
        [Serializable] private class ObjectSpec
        {
            public string name, sprite, group;
            public float x, y;
            public int order;
            public int depthOrder;
            public bool flipX;
        }
        [Serializable] private class ColliderSpec
        {
            public string name, group;
            public float x, y, width, height;
        }
        [Serializable] private class MarkerSpec { public string name; public float x, y; }
        [Serializable] private class CameraSpec { public float x, y, size; }

        /// <summary>Apply architecture revisions while retaining unrelated live scene edits.</summary>
        public static void UpdateBuildings()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before updating buildings.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) throw new InvalidOperationException("Farm Environment root was not found.");
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(FarmPath + "/Design/farm-layout.json", Encoding.UTF8));
            ValidatePlan(plan);

            // Save a copy BEFORE importing or replacing anything, including any
            // unsaved manual changes already present in the user's open scene.
            EnsureFolder("Assets/Scenes/Backups");
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeBuildingUpdate.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true))
                throw new IOException("Could not preserve the current scene before the update.");
            SceneManager.SetActiveScene(scene);
            var spriteMap = ImportAtlasCopies(plan.sprites);
            var material = GetMaterial();
            var tileAssets = GetTiles(plan.tileLayers, spriteMap);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Improve farm buildings");
            var replaced = new[] { "Buildings/Cottage", "Buildings/Barn", "Buildings/Seed Shop", "Buildings/Storage Shed",
                "Seed Shop/Display", "Storage Shed/Supplies", "Cottage/Flowers" };
            foreach (var path in replaced)
            {
                var old = root.transform.Find(path);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            var buildingCollisions = root.transform.Find("Collisions/Buildings");
            if (buildingCollisions != null)
            {
                var names = new HashSet<string> { "Cottage footprint", "Barn footprint", "Seed Shop footprint", "Storage Shed footprint" };
                foreach (Transform child in buildingCollisions.Cast<Transform>().ToArray())
                    if (names.Contains(child.name)) Undo.DestroyObjectImmediate(child.gameObject);
            }

            // Remove only ground decoration obscured by the two new buildings.
            foreach (var path in new[] { "Nature/Ground cover", "Barnyard/Grass", "Nature/Shrubs" })
            {
                var decoration = root.transform.Find(path);
                if (decoration == null) continue;
                foreach (var renderer in decoration.GetComponentsInChildren<SpriteRenderer>())
                {
                    var bounds = renderer.bounds;
                    if (bounds.Intersects(new Bounds(new Vector3(-6, 4, 0), new Vector3(4.5f, 4.8f, 2)))
                        || bounds.Intersects(new Bounds(new Vector3(23.5f, -1.3f, 0), new Vector3(3.4f, 5.2f, 2))))
                        Undo.DestroyObjectImmediate(renderer.gameObject);
                }
            }
            var parents = new Dictionary<string, Transform> { [""] = root.transform };
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == root.transform) continue;
                string path = AnimationUtility.CalculateTransformPath(transform, root.transform);
                // Sprite leaves may share descriptive names; only containers matter.
                if (!parents.ContainsKey(path)) parents.Add(path, transform);
            }
            foreach (var group in plan.groups)
            {
                var parent = Parent(group.name, parents);
                Undo.RegisterCreatedObjectUndo(parent.gameObject, "Create building");
                var sorting = Undo.AddComponent<SortingGroup>(parent.gameObject);
                sorting.sortingOrder = group.sortingOrder;
                if (group.depthOrder != 0) FarmSortingEditor.SetSourceDepth(parent.gameObject, group.depthOrder);
            }
            foreach (var spec in plan.objects.Where(o => o.group.StartsWith("Buildings/", StringComparison.Ordinal)
                || o.group == "Seed Shop/Display" || o.group == "Storage Shed/Supplies"))
            {
                var go = MakeObject(spec.name, Parent(spec.group, parents));
                go.transform.position = new Vector3(spec.x, spec.y, 0);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = spriteMap[spec.sprite];
                renderer.sharedMaterial = material;
                renderer.sortingOrder = spec.order;
                if (spec.depthOrder != 0) FarmSortingEditor.SetSourceDepth(go, spec.depthOrder);
                renderer.flipX = spec.flipX;
                renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                Undo.RegisterCreatedObjectUndo(go, "Place building part");
            }
            foreach (var spec in plan.colliders.Where(c => c.group == "Collisions/Buildings" || c.name == "Seed display crates"))
            {
                var parent = Parent(spec.group, parents);
                if (spec.name == "Seed display crates")
                {
                    var old = parent.Find(spec.name);
                    if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                }
                var go = MakeObject(spec.name, parent);
                go.transform.position = new Vector3(spec.x, spec.y, 0);
                go.AddComponent<BoxCollider2D>().size = new Vector2(spec.width, spec.height);
                Undo.RegisterCreatedObjectUndo(go, "Fit building collision");
            }
            foreach (var spec in plan.markers.Where(m => m.name == "Seed Shop Entrance" || m.name == "Storage Shed Entrance"))
            {
                var parent = Parent("Layout Markers", parents);
                var found = parent.Find(spec.name);
                var go = found != null ? found.gameObject : MakeObject(spec.name, parent);
                if (found == null) Undo.RegisterCreatedObjectUndo(go, "Add entrance marker");
                else Undo.RecordObject(go.transform, "Move entrance marker");
                go.transform.position = new Vector3(spec.x, spec.y, 0);
            }

            var pathMap = root.transform.Find("Terrain/03 Earthen paths/Tiles")?.GetComponent<Tilemap>();
            if (pathMap == null) throw new InvalidOperationException("The existing path Tilemap is missing.");
            Undo.RegisterCompleteObjectUndo(pathMap, "Connect the seed shop");
            foreach (var tile in plan.tileLayers.First(l => l.name == "03 Earthen paths").tiles)
                if (tile.x >= -16 && tile.x <= -8 && tile.y >= -4 && tile.y <= 7)
                    pathMap.SetTile(new Vector3Int(tile.x, tile.y, 0), tileAssets[tile.sprite]);
            Undo.CollapseUndoOperations(undoGroup);
            FarmSortingEditor.Normalize(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save updated FarmLevel.");
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log("Farm buildings updated. Unrelated scene objects retained; prior live scene saved to " + backup);
        }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before building the environment.");
            var existing = SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded && existing.isDirty)
                throw new InvalidOperationException("FarmLevel has unsaved edits. Save it before rebuilding.");

            var jsonPath = FarmPath + "/Design/farm-layout.json";
            if (!File.Exists(jsonPath)) throw new FileNotFoundException("Farm layout has not been authored.", jsonPath);
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(jsonPath, Encoding.UTF8));
            ValidatePlan(plan);

            EnsureFolder(FarmPath + "/Atlases");
            EnsureFolder(FarmPath + "/Tiles");
            EnsureFolder(FarmPath + "/Materials");
            var spriteMap = ImportAtlasCopies(plan.sprites);
            var material = GetMaterial();
            var tileAssets = GetTiles(plan.tileLayers, spriteMap);

            // Build in an isolated additive scene. User scenes, including unsaved
            // SampleScene edits, remain intact throughout authoring.
            var previousActive = SceneManager.GetActiveScene();
            var staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(staging);
            var root = MakeObject(RootName, null);
            var parents = new Dictionary<string, Transform> { [""] = root.transform };
            try
            {
                foreach (var group in plan.groups)
                {
                    var sorting = Parent(group.name, parents).gameObject.AddComponent<SortingGroup>();
                    sorting.sortingOrder = group.sortingOrder;
                    if (group.depthOrder != 0) FarmSortingEditor.SetSourceDepth(sorting.gameObject, group.depthOrder);
                }

                foreach (var spec in plan.tileLayers)
                {
                    var gridObject = MakeObject(spec.name, Parent("Terrain", parents));
                    var grid = gridObject.AddComponent<Grid>();
                    grid.cellSize = new Vector3(spec.cellSize, spec.cellSize, 1);
                    var go = MakeObject("Tiles", gridObject.transform);
                    var map = go.AddComponent<Tilemap>();
                    var renderer = go.AddComponent<TilemapRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.sortingOrder = spec.order;
                    renderer.mode = TilemapRenderer.Mode.Chunk;
                    var positions = new Vector3Int[spec.tiles.Length];
                    var tiles = new TileBase[spec.tiles.Length];
                    for (var i = 0; i < spec.tiles.Length; i++)
                    {
                        var tile = spec.tiles[i];
                        positions[i] = new Vector3Int(tile.x, tile.y, 0);
                        tiles[i] = tileAssets[tile.sprite];
                    }
                    map.SetTiles(positions, tiles);
                    map.CompressBounds();
                }

                foreach (var spec in plan.objects)
                {
                    var go = MakeObject(spec.name, Parent(spec.group, parents));
                    go.transform.position = new Vector3(spec.x, spec.y, 0);
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = spriteMap[spec.sprite];
                    renderer.sharedMaterial = material;
                    renderer.sortingOrder = spec.order;
                    if (spec.depthOrder != 0) FarmSortingEditor.SetSourceDepth(go, spec.depthOrder);
                    renderer.flipX = spec.flipX;
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                }

                foreach (var spec in plan.colliders)
                {
                    var go = MakeObject(spec.name, Parent(spec.group, parents));
                    go.transform.position = new Vector3(spec.x, spec.y, 0);
                    go.AddComponent<BoxCollider2D>().size = new Vector2(spec.width, spec.height);
                }
                foreach (var spec in plan.markers)
                {
                    var go = MakeObject(spec.name, Parent("Layout Markers", parents));
                    go.transform.position = new Vector3(spec.x, spec.y, 0);
                }

                var cameraObject = MakeObject("Farm Camera", root.transform);
                var camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                camera.transform.position = new Vector3(plan.camera.x, plan.camera.y, -10);
                camera.orthographic = true;
                camera.orthographicSize = plan.camera.size;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 100;
                camera.depth = 10;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(63, 83, 56, 255);
                camera.cullingMask = ~0;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.transparencySortMode = TransparencySortMode.CustomAxis;
                camera.transparencySortAxis = Vector3.up;
                var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.renderShadows = false;
                cameraObject.AddComponent<AudioListener>();

                // Only replace the scene after the staged environment exists.
                FarmSortingEditor.Normalize(staging);
                if (existing.IsValid() && existing.isLoaded)
                    EditorSceneManager.CloseScene(existing, true);
                if (!EditorSceneManager.SaveScene(staging, ScenePath))
                    throw new IOException("Unity could not save " + ScenePath);
                var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
                buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
                AssetDatabase.SaveAssets();
                for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var other = SceneManager.GetSceneAt(i);
                    if (other.path == "Assets/Scenes/SampleScene.unity" && !other.isDirty)
                        EditorSceneManager.CloseScene(other, true);
                }
                Selection.activeGameObject = root;
                foreach (SceneView view in SceneView.sceneViews)
                {
                    view.in2DMode = true;
                    view.Frame(new Bounds(Vector3.zero, new Vector3(plan.width, plan.height, 1)), true);
                }
                Debug.Log($"FarmLevel saved: {plan.objects.Length} sprite objects, {plan.tileLayers.Length} tilemaps, {plan.colliders.Length} colliders. No characters, animals or enemies.");
            }
            catch
            {
                if (staging.IsValid() && staging.isLoaded && staging.path != ScenePath)
                    EditorSceneManager.CloseScene(staging, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                throw;
            }
        }

        private static void ValidatePlan(Plan plan)
        {
            if (plan == null || plan.width < 1 || plan.height < 1 || plan.camera == null)
                throw new InvalidDataException("Invalid farm dimensions or camera.");
            var names = new HashSet<string>();
            foreach (var spec in plan.sprites)
            {
                if (!spec.atlas.StartsWith("Assets/Tileset/", StringComparison.Ordinal) || !File.Exists(spec.atlas))
                    throw new InvalidDataException("Only supplied tileset artwork may be used: " + spec.atlas);
                if (!names.Add(spec.name) || spec.w <= 0 || spec.h <= 0 || spec.x < 0 || spec.y < 0)
                    throw new InvalidDataException("Invalid sprite rectangle/name: " + spec.name);
            }
            foreach (var obj in plan.objects)
                if (!names.Contains(obj.sprite)) throw new InvalidDataException("Missing sprite " + obj.sprite);
            foreach (var layer in plan.tileLayers)
            {
                if (layer.cellSize <= 0) throw new InvalidDataException("Invalid grid size.");
                foreach (var tile in layer.tiles)
                    if (!names.Contains(tile.sprite)) throw new InvalidDataException("Missing tile " + tile.sprite);
            }
        }

        private static Dictionary<string, Sprite> ImportAtlasCopies(SpriteSpec[] specs)
        {
            var result = new Dictionary<string, Sprite>();
            foreach (var atlas in specs.GroupBy(s => s.atlas))
            {
                var target = FarmPath + "/Atlases/" + Path.GetFileName(atlas.Key);
                File.Copy(atlas.Key, target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(target);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 16;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteExtrude = 0;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                provider.InitSpriteEditorDataProvider();
                var oldIds = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
                var rects = atlas.Select(s =>
                {
                    if (s.x + s.w > texture.width || s.y + s.h > texture.height)
                        throw new InvalidDataException("Sprite exceeds atlas: " + s.name);
                    return new SpriteRect
                    {
                        name = s.name,
                        rect = new Rect(s.x, texture.height - s.y - s.h, s.w, s.h),
                        alignment = SpriteAlignment.Custom,
                        pivot = new Vector2(s.pivotX, s.pivotY),
                        spriteID = oldIds.TryGetValue(s.name, out var id) ? id : StableGuid(target + ":" + s.name)
                    };
                }).ToArray();
                // Keep old sub-sprites valid for user objects and scene backups.
                var currentNames = new HashSet<string>(rects.Select(r => r.name));
                var allRects = provider.GetSpriteRects().Where(r => !currentNames.Contains(r.name)).Concat(rects).ToArray();
                provider.SetSpriteRects(allRects);
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(
                    allRects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(target).OfType<Sprite>())
                    if (currentNames.Contains(sprite.name)) result.Add(sprite.name, sprite);
            }
            if (result.Count != specs.Length)
                throw new InvalidDataException($"Expected {specs.Length} sprites; Unity imported {result.Count}.");
            return result;
        }

        private static UnityEngine.GUID StableGuid(string value)
        {
            using (var md5 = MD5.Create())
                return new UnityEngine.GUID(BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant());
        }

        private static Material GetMaterial()
        {
            var path = FarmPath + "/Materials/Farm Sprite Unlit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) throw new InvalidOperationException("URP 2D unlit shader is unavailable.");
            material = new Material(shader) { name = "Farm Sprite Unlit" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Dictionary<string, Tile> GetTiles(LayerSpec[] layers, Dictionary<string, Sprite> sprites)
        {
            var result = new Dictionary<string, Tile>();
            foreach (var name in layers.SelectMany(l => l.tiles).Select(t => t.sprite).Distinct())
            {
                var path = FarmPath + "/Tiles/" + name + ".asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = name;
                    AssetDatabase.CreateAsset(tile, path);
                }
                tile.sprite = sprites[name];
                tile.colliderType = Tile.ColliderType.None;
                tile.color = Color.white;
                EditorUtility.SetDirty(tile);
                result.Add(name, tile);
            }
            return result;
        }

        private static GameObject MakeObject(string name, Transform parent)
        {
            var go = new GameObject(name) { layer = FarmLayer };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Transform Parent(string path, Dictionary<string, Transform> parents)
        {
            if (parents.TryGetValue(path ?? "", out var found)) return found;
            var slash = path.LastIndexOf('/');
            var prefix = slash < 0 ? "" : path.Substring(0, slash);
            var name = slash < 0 ? path : path.Substring(slash + 1);
            var created = MakeObject(name, Parent(prefix, parents)).transform;
            parents.Add(path, created);
            return created;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
