using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farm.EditorTools
{
    public static class FarmPlayerBuilder
    {
        private const string Folder = "Assets/Farm/Player";
        private static readonly string[] Directions = { "Down", "Left", "Right", "Up" };
        private static readonly int[] Rows = { 2, 3, 1, 0 };

        [MenuItem("Tools/Farm/Set Up Player")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before setting up the player.");
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity", OpenSceneMode.Additive);
            Directory.CreateDirectory("Assets/Scenes/Backups");
            AssetDatabase.Refresh();
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforePlayer.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed.");

            Directory.CreateDirectory(Folder + "/Sprites");
            Directory.CreateDirectory(Folder + "/Animations");
            AssetDatabase.Refresh();
            Sprite[] idle = ImportSheet("Idle", 5);
            Sprite[] run = ImportSheet("Run", 8);
            string controllerPath = Folder + "/BaseCharacter.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Direction", AnimatorControllerParameterType.Int);
                var machine = controller.layers[0].stateMachine;
                for (int direction = 0; direction < 4; direction++)
                for (int moving = 0; moving < 2; moving++)
                {
                    string name = (moving == 1 ? "Run" : "Idle") + Directions[direction];
                    var state = machine.AddState(name, new Vector3(moving * 260, direction * 80));
                    state.motion = CreateClip(name, moving == 1 ? run : idle, Rows[direction], moving == 1 ? 8 : 5, moving == 1 ? 10 : 6);
                    if (direction == 0 && moving == 0) machine.defaultState = state;
                    var transition = machine.AddAnyStateTransition(state);
                    transition.hasExitTime = false;
                    transition.duration = 0;
                    transition.canTransitionToSelf = false;
                    transition.AddCondition(moving == 1 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Moving");
                    transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
                }
            }

            var roots = scene.GetRootGameObjects();
            var player = roots.SelectMany(root => root.GetComponentsInChildren<FarmPlayerController>(true)).FirstOrDefault();
            if (player == null)
            {
                var go = new GameObject("Player - Bunny");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create farm player");
                go.tag = "Player";
                var spawn = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t => t.name.StartsWith("Player Spawn", StringComparison.Ordinal));
                go.transform.position = spawn != null ? spawn.position : new Vector3(-15, 5.375f, 0);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = idle[Rows[0] * 5];
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Farm/Materials/Farm Sprite Unlit.mat");
                renderer.sortingOrder = 100;
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var body = go.AddComponent<Rigidbody2D>();
                body.gravityScale = 0;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var collider = go.AddComponent<CapsuleCollider2D>();
                collider.direction = CapsuleDirection2D.Horizontal;
                collider.size = new Vector2(0.65f, 0.3f);
                collider.offset = new Vector2(0, 0.15f);
                string physicsPath = Folder + "/PlayerFriction.physicsMaterial2D";
                var friction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(physicsPath);
                if (friction == null)
                {
                    friction = new PhysicsMaterial2D("PlayerFriction") { friction = 0, bounciness = 0 };
                    AssetDatabase.CreateAsset(friction, physicsPath);
                }
                collider.sharedMaterial = friction;
                player = go.AddComponent<FarmPlayerController>();
            }

            ApplyBunnySkin(player, controller, idle, run);

            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(c => c.name == "Farm Camera");
            if (camera == null) throw new InvalidOperationException("Farm Camera was not found.");
            Undo.RecordObject(camera, "Set gameplay camera zoom");
            var follow = camera.GetComponent<FarmCameraFollow>();
            if (follow == null) follow = Undo.AddComponent<FarmCameraFollow>(camera.gameObject);
            follow.Target = player.transform;
            camera.orthographicSize = 4.5f;
            follow.SnapToTarget();
            EditorUtility.SetDirty(follow);
            FarmSortingEditor.Normalize(scene);
            PrefabUtility.SaveAsPrefabAsset(player.gameObject, Folder + "/BaseCharacterPlayer.prefab");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the player scene.");
            SceneManager.SetActiveScene(scene);
            Selection.activeGameObject = player.gameObject;
            Debug.Log("Farm player ready: WASD, eight directional Idle/Run clips, follow camera.");
        }

        [MenuItem("Tools/Farm/Replace Player Skin With Bunny")]
        public static void ReplaceSkin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before replacing the player skin.");
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity", OpenSceneMode.Additive);
            var player = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FarmPlayerController>(true)).Single();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/BaseCharacter.controller");
            if (controller == null) throw new InvalidOperationException("Set up the player first.");
            ApplyBunnySkin(player, controller, ImportSheet("Idle", 5), ImportSheet("Run", 8));
            PrefabUtility.SaveAsPrefabAsset(player.gameObject, Folder + "/BaseCharacterPlayer.prefab");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the Bunny player scene.");
            Debug.Log("Bunny skin ready: directional Idle/Run animations connected to WASD.");
        }

        private static void ApplyBunnySkin(FarmPlayerController player, AnimatorController controller, Sprite[] idle, Sprite[] run)
        {
            for (int direction = 0; direction < 4; direction++)
            for (int moving = 0; moving < 2; moving++)
            {
                string name = (moving == 1 ? "Run" : "Idle") + Directions[direction];
                var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == name).state;
                state.motion = CreateClip(name, moving == 1 ? run : idle, Rows[direction], moving == 1 ? 8 : 5, moving == 1 ? 10 : 6);
                EditorUtility.SetDirty(state);
            }
            player.gameObject.name = "Player - Bunny";
            player.GetComponent<SpriteRenderer>().sprite = idle[Rows[0] * 5];
            player.GetComponent<Animator>().runtimeAnimatorController = controller;
            EditorUtility.SetDirty(controller);
        }

        private static Sprite[] ImportSheet(string action, int columns)
        {
            string path = Folder + "/Sprites/Bunny_" + action + ".png";
            File.Copy("Assets/Sprites/Characters/BUNNY/" + action.ToUpperInvariant() + "/Bunny_" + action + ".png", path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new SpriteRect[columns * 4];
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < columns; column++)
            {
                string name = action + "_" + row + "_" + column;
                rects[row * columns + column] = new SpriteRect
                {
                    name = name,
                    rect = new Rect(column * 48, (3 - row) * 48, 48, 48),
                    alignment = SpriteAlignment.Custom,
                    // Align feet consistently; automatic tight slices shift between frames.
                    pivot = new Vector2(0.5f, 15f / 48f),
                    spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate()
                };
            }
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            return rects.Select(r => sprites[r.name]).ToArray();
        }

        private static AnimationClip CreateClip(string name, Sprite[] sheet, int row, int columns, int fps)
        {
            string path = Folder + "/Animations/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name, frameRate = fps };
                AssetDatabase.CreateAsset(clip, path);
            }
            var frames = new ObjectReferenceKeyframe[columns + 1];
            for (int i = 0; i <= columns; i++)
                frames[i] = new ObjectReferenceKeyframe { time = (float)i / fps, value = sheet[row * columns + i % columns] };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), frames);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}
