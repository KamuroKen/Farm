using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmToolAnimationSetup
    {
        private const string Folder = "Assets/Farm/Player";
        static FarmToolAnimationSetup() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Library/FarmTools.request")) return;
            File.Delete("Library/FarmTools.request");
            try { Build(); File.WriteAllText("BuildArtifacts/farm-tools-setup.txt", "PASS"); }
            catch (Exception e) { File.WriteAllText("BuildArtifacts/farm-tools-setup.txt", e.ToString()); }
        }
        [MenuItem("Tools/Farm/Set Up Bunny Tool Animations")]
        public static void Build()
        {
            var path = AssetDatabase.FindAssets("t:AnimatorController", new[]{Folder}).Select(AssetDatabase.GUIDToAssetPath).First();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!controller.parameters.Any(p => p.name == "Working")) controller.AddParameter("Working", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            foreach (var transition in machine.anyStateTransitions)
                if (!transition.conditions.Any(c => c.parameter == "Working")) transition.AddCondition(AnimatorConditionMode.IfNot, 0, "Working");
            string[] directions = { "Down", "Left", "Right", "Up" };
            int[] rows = { 3, 2, 1, 0 };
            foreach (string action in new[]{"Scythe", "WateringCan"})
            {
                var sheet = ImportSheet(action, 9);
                for (int d = 0; d < 4; d++)
                {
                    string name = (action == "WateringCan" ? "Watering" : action) + directions[d];
                    string clipPath = Folder + "/Animations/" + name + ".anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, clipPath); }
                    clip.frameRate = 9;
                    var keys = Enumerable.Range(0,10).Select(i => new ObjectReferenceKeyframe {time = i / 9f, value = sheet[rows[d]*9 + Mathf.Min(i,8)]}).ToArray();
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
                    var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, settings);
                    var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
                    state.motion = clip;
                    EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state);
                }
            }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        }
        private static Sprite[] ImportSheet(string action, int columns)
        {
            string path = Folder + "/Sprites/Bunny_" + action + ".png";
            File.Copy("Assets/Sprites/Characters/BUNNY/" + (action == "WateringCan" ? "WATERING CAN" : action.ToUpperInvariant()) + "/Bunny_" + action + ".png", path, true);
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


    }
}
