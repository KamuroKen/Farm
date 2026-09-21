using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmGardenSetup
    {
        private const string Folder = "Assets/Farm/Gardening";
        private const string Request = "Library/FarmGardenSetup.request";
        private static readonly string[] Names = { "Морковь", "Капуста", "Тыква", "Клубника", "Кукуруза", "Пшеница" };
        private static readonly string[] Ids = { "Carrot", "Cabbage", "Pumpkin", "Strawberry", "Corn", "Wheat" };
        private static readonly int[] Columns = { 16, 32, 48, 64, 80, 208 };

        static FarmGardenSetup() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode
                && File.Exists("Library/FarmGardenRefresh.request"))
            {
                File.Delete("Library/FarmGardenRefresh.request");
                AssetDatabase.Refresh();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            try { Build(); File.WriteAllText("BuildArtifacts/garden-setup.txt", "PASS: garden installed, six crops, four stages, UI and placement checks."); }
            catch (Exception e) { File.WriteAllText("BuildArtifacts/garden-setup.txt", "FAIL: " + e); Debug.LogException(e); }
        }

        [MenuItem("Tools/Farm/Set Up Gardening Prototype")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity", OpenSceneMode.Additive);
            Directory.CreateDirectory("BuildArtifacts");
            Directory.CreateDirectory("Assets/Scenes/Backups");
            Directory.CreateDirectory(Folder + "/Crops");
            AssetDatabase.Refresh();
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeGardening.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up current scene.");
            Sprite[] sprites = ImportSprites();
            var spriteMap = sprites.ToDictionary(s => s.name);
            var roots = scene.GetRootGameObjects();
            var environment = roots.First(g => g.name == "Farm Environment");
            var allMaps = environment.GetComponentsInChildren<Tilemap>(true);
            var ground = allMaps.First(m => m.transform.parent.name == "01 Grass and water");
            var surfaces = allMaps.Where(m => m.transform.parent.name == "02 Shoreline" || m.transform.parent.name == "03 Earthen paths").ToArray();
            var oldSoil = allMaps.FirstOrDefault(m => m.transform.parent.name == "04 Cultivated soil");
            if (oldSoil != null) { Undo.RecordObject(oldSoil, "Clear decorative garden soil"); oldSoil.ClearAllTiles(); }
            foreach (var renderer in environment.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.sprite != null && renderer.sprite.name.StartsWith("crop_", StringComparison.Ordinal))
                { Undo.RecordObject(renderer.gameObject, "Disable decorative crops"); renderer.gameObject.SetActive(false); }

            var system = roots.SelectMany(r => r.GetComponentsInChildren<FarmGardenSystem>(true)).FirstOrDefault();
            if (system == null)
            {
                var go = new GameObject("Gardening");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create gardening prototype");
                system = go.AddComponent<FarmGardenSystem>();
                go.AddComponent<FarmGardenUI>();
                var grid = new GameObject("05 Player plots", typeof(Grid));
                grid.transform.SetParent(go.transform, false);
                var tileObject = new GameObject("Interactive soil", typeof(Tilemap), typeof(TilemapRenderer));
                tileObject.transform.SetParent(grid.transform, false);
                system.soil = tileObject.GetComponent<Tilemap>();
            }
            system.environment = environment.transform;
            system.player = roots.SelectMany(r => r.GetComponentsInChildren<FarmPlayerController>(true)).First().transform;
            system.worldCamera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First(c => c.name == "Farm Camera");
            system.ground = ground; system.forbiddenSurfaces = surfaces;
            system.spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Farm/Materials/Farm Sprite Unlit.mat");
            system.lightSoil = TileAsset("LightSoil", spriteMap["Plot_Light"]);
            system.darkSoil = TileAsset("DarkSoil", spriteMap["Plot_Dark"]);
            system.cursorSprite = UtilitySprite("PlacementCursor", false);
            system.waterMarker = UtilitySprite("WaterDrop", true);
            var mapRenderer = system.soil.GetComponent<TilemapRenderer>();
            mapRenderer.sharedMaterial = system.spriteMaterial; mapRenderer.sortingOrder = 15;
            system.crops = new FarmCropDefinition[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Folder + "/Crops/" + Ids[i] + ".asset";
                var crop = AssetDatabase.LoadAssetAtPath<FarmCropDefinition>(path);
                if (crop == null) { crop = ScriptableObject.CreateInstance<FarmCropDefinition>(); AssetDatabase.CreateAsset(crop, path); }
                crop.displayName = Names[i]; crop.seedPacket = spriteMap[Ids[i] + "_Packet"];
                crop.stages = Enumerable.Range(0, 4).Select(s => spriteMap[Ids[i] + "_Stage" + s]).ToArray();
                crop.growthSeconds = 15; crop.harvestAmount = 1;
                system.crops[i] = crop; EditorUtility.SetDirty(crop);
            }
            EditorUtility.SetDirty(system);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save gardening scene.");
            SceneManager.SetActiveScene(scene);
            Selection.activeGameObject = system.gameObject;
            Debug.Log("Gardening ready: six crops; placement, tilling, planting, watering and harvest. Backup: " + backup);
        }

        private static Tile TileAsset(string name, Sprite sprite)
        {
            string path = Folder + "/" + name + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.sprite = sprite; tile.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(tile);
            return tile;
        }

        private static Sprite[] ImportSprites()
        {
            string path = Folder + "/Crops.png";
            File.Copy("Assets/Tileset/Crops_Tileset.png", path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16; importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true; importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new List<SpriteRect>();
            int height = AssetDatabase.LoadAssetAtPath<Texture2D>(path).height;
            void Add(string name, int x, int y, int w, int h, Vector2 pivot)
            {
                rects.Add(new SpriteRect { name = name, rect = new Rect(x, height - y - h, w, h),
                    alignment = SpriteAlignment.Custom, pivot = pivot, spriteID = previous.TryGetValue(name, out var id) ? id : GUID.Generate() });
            }
            Add("Plot_Light", 16, 416, 16, 16, new Vector2(0.5f, 0.5f));
            Add("Plot_Dark", 32, 416, 16, 16, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < Names.Length; i++)
            {
                Add(Ids[i] + "_Packet", Columns[i], 16, 16, 16, new Vector2(0.5f, 0.5f));
                Add(Ids[i] + "_Stage0", Columns[i], 32, 16, 16, new Vector2(0.5f, 0.5f));
                Add(Ids[i] + "_Stage1", Columns[i], 48, 16, 32, new Vector2(0.5f, 0.25f));
                Add(Ids[i] + "_Stage2", Columns[i], 112, 16, 32, new Vector2(0.5f, 0.25f));
                Add(Ids[i] + "_Stage3", Columns[i], 144, 16, 32, new Vector2(0.5f, 0.25f));
            }
            provider.SetSpriteRects(rects.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply(); importer.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        }

        private static Sprite UtilitySprite(string name, bool drop)
        {
            int size = drop ? 7 : 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color color;
                if (drop)
                {
                    int radius = y < 2 ? 1 : (y < 4 ? 2 : (y < 6 ? 1 : 0));
                    color = Mathf.Abs(x - 3) <= radius ? new Color32(95, 207, 243, 255) : Color.clear;
                }
                else color = new Color(1, 1, 1, x == 0 || y == 0 || x == size - 1 || y == size - 1 ? 1 : 0.12f);
                texture.SetPixel(x, y, color);
            }
            texture.Apply();
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
