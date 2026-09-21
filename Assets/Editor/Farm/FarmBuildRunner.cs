using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    /// <summary>Runs explicit local requests in the already-open Unity editor.</summary>
    [InitializeOnLoad]
    public static class FarmBuildRunner
    {
        private const string BuilderTypeName = "Farm.EditorTools.FarmLevelBuilder";
        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static readonly string RequestPath = Path.Combine(ProjectRoot, "Library", "FarmBuild.request");
        private static readonly string ArtifactDirectory = Path.Combine(ProjectRoot, "BuildArtifacts");
        private static double nextPoll;
        private static double captureAfter;
        private static double captureDeadline;
        private static bool busy;
        private static BuildReport pendingReport;

        [Serializable]
        private sealed class BuildReport
        {
            public bool success;
            public string operation;
            public string completedUtc;
            public string scenePath;
            public string sceneName;
            public string cameraName;
            public float orthographicSize;
            public int transforms;
            public int spriteRenderers;
            public int tilemaps;
            public int colliders2D;
            public int missingScripts;
            public string overviewImage;
            public string detailImage;
            public string barnyardImage;
            public string[] forbiddenAssetReferences = Array.Empty<string>();
            public string[] errors = Array.Empty<string>();
            public string[] warnings = Array.Empty<string>();
        }

        static FarmBuildRunner()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        [MenuItem("Tools/Farm/Runner/Build Level")]
        public static void RequestBuild() => QueueRequest("build");

        [MenuItem("Tools/Farm/Runner/Update Buildings")]
        public static void RequestBuildingUpdate() => QueueRequest("buildings");

        [MenuItem("Tools/Farm/Runner/Capture Level")]
        public static void RequestCapture() => QueueRequest("capture");

        [MenuItem("Tools/Farm/Runner/Validate Level")]
        public static void RequestValidation() => QueueRequest("validate");

        private static void QueueRequest(string operation)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RequestPath));
            File.WriteAllText(RequestPath, operation);
        }

        private static bool EditorIsReady()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isUpdating
                && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void Update()
        {
            if (!EditorIsReady()) return;

            if (busy)
            {
                if (pendingReport != null && EditorApplication.timeSinceStartup >= captureAfter)
                    FinishCapture();
                return;
            }

            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(RequestPath)) return;

            var report = new BuildReport();
            try
            {
                report.operation = File.ReadAllText(RequestPath).Trim().ToLowerInvariant();
                File.Delete(RequestPath);
                busy = true;

                switch (report.operation)
                {
                    case "sorting":
                        FarmSortingEditor.Apply();
                        break;
                    case "player":
                        FarmPlayerBuilder.Build();
                        break;
                    case "build":
                        InvokeBuilder();
                        break;
                    case "buildings":
                        InvokeBuilder("UpdateBuildings");
                        break;
                    case "capture":
                        break;
                    case "validate":
                        ValidateScene(report);
                        Complete(report);
                        return;
                    default:
                        throw new InvalidOperationException("Unknown FarmBuild.request operation: " + report.operation);
                }

                pendingReport = report;
                captureAfter = EditorApplication.timeSinceStartup + 1.0;
                captureDeadline = EditorApplication.timeSinceStartup + 30.0;
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception exception)
            {
                AddError(report, Unwrap(exception).ToString());
                Complete(report);
            }
        }

        private static void InvokeBuilder(string methodName = "Build")
        {
            Type builder = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(BuilderTypeName, false))
                .FirstOrDefault(type => type != null);
            if (builder == null)
                throw new InvalidOperationException("Builder type was not compiled: " + BuilderTypeName);

            MethodInfo build = builder.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (build == null)
                throw new MissingMethodException(BuilderTypeName, methodName + "()");
            build.Invoke(null, null);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        private static void FinishCapture()
        {
            // The pipeline is initialized by an editor/player render after a scene has been created.
            if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline)
                && EditorApplication.timeSinceStartup < captureDeadline)
            {
                captureAfter = EditorApplication.timeSinceStartup + 0.5;
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }

            BuildReport report = pendingReport;
            try
            {
                ValidateScene(report);
                Camera camera = FindFarmCamera(SceneManager.GetActiveScene());
                if (camera == null) throw new InvalidOperationException("The active scene has no farm camera.");
                if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline))
                    throw new InvalidOperationException("The Universal Render Pipeline did not initialize for capture.");

                report.cameraName = camera.name;
                report.orthographicSize = camera.orthographicSize;
                Directory.CreateDirectory(ArtifactDirectory);
                string overview = Path.Combine(ArtifactDirectory, "FarmLevel-Unity.png");
                CaptureToFile(camera, 1536, 1152, overview);
                report.overviewImage = overview;

                GameObject detailObject = null;
                try
                {
                    detailObject = UnityEngine.Object.Instantiate(camera.gameObject);
                    detailObject.name = "Farm Capture Camera (temporary)";
                    detailObject.hideFlags = HideFlags.HideAndDontSave;
                    foreach (AudioListener listener in detailObject.GetComponentsInChildren<AudioListener>(true))
                        listener.enabled = false;
                    Camera detail = detailObject.GetComponent<Camera>();
                    detail.enabled = false;
                    detail.orthographic = true;
                    detail.orthographicSize = 6.75f;
                    detail.transform.position = new Vector3(-10.5f, 6.75f, -10f);
                    string detailPath = Path.Combine(ArtifactDirectory, "FarmLevel-Detail.png");
                    CaptureToFile(detail, 1536, 864, detailPath);
                    report.detailImage = detailPath;
                    detail.orthographicSize = 9f;
                    detail.transform.position = new Vector3(16f, 4f, -10f);
                    string barnyardPath = Path.Combine(ArtifactDirectory, "FarmLevel-Barnyard.png");
                    CaptureToFile(detail, 1536, 864, barnyardPath);
                    report.barnyardImage = barnyardPath;
                }
                finally
                {
                    if (detailObject != null) UnityEngine.Object.DestroyImmediate(detailObject);
                }
            }
            catch (Exception exception)
            {
                AddError(report, Unwrap(exception).ToString());
            }
            Complete(report);
        }

        private static Camera FindFarmCamera(Scene scene)
        {
            Camera[] cameras = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            return cameras.FirstOrDefault(camera => camera.name == "Farm Camera")
                ?? cameras.FirstOrDefault(camera => camera.name == "Main Camera")
                ?? cameras.FirstOrDefault(camera => camera.enabled)
                ?? cameras.FirstOrDefault();
        }

        private static void CaptureToFile(Camera camera, int width, int height, string path)
        {
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Farm Screenshot Target",
                antiAliasing = 1,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            Texture2D image = null;
            try
            {
                target.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                var colors = new HashSet<uint>();
                Color32[] pixels = image.GetPixels32();
                for (int i = 0; i < pixels.Length && colors.Count < 16; i += 7)
                {
                    Color32 color = pixels[i];
                    colors.Add((uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24));
                }
                if (colors.Count < 8)
                    throw new InvalidOperationException("Camera capture appears empty or nearly uniform: " + camera.name);

                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void ValidateScene(BuildReport report)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("There is no loaded active scene to validate.");

            report.scenePath = scene.path;
            report.sceneName = scene.name;
            GameObject[] roots = scene.GetRootGameObjects();
            Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            SpriteRenderer[] sprites = roots.SelectMany(root => root.GetComponentsInChildren<SpriteRenderer>(true)).ToArray();
            Tilemap[] tilemaps = roots.SelectMany(root => root.GetComponentsInChildren<Tilemap>(true)).ToArray();
            report.transforms = transforms.Length;
            report.spriteRenderers = sprites.Length;
            report.tilemaps = tilemaps.Length;
            report.colliders2D = roots.Sum(root => root.GetComponentsInChildren<Collider2D>(true).Length);
            report.missingScripts = transforms.Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));

            var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpriteRenderer sprite in sprites)
                InspectSprite(sprite.sprite, forbidden);
            foreach (Tilemap tilemap in tilemaps)
                foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.HasTile(position)) InspectSprite(tilemap.GetSprite(position), forbidden);
            report.forbiddenAssetReferences = forbidden.OrderBy(path => path).ToArray();

            if (report.missingScripts > 0) AddError(report, "Active scene contains " + report.missingScripts + " missing scripts.");
            if (forbidden.Count > 0) AddError(report, "Active scene references forbidden Assets/Sprites artwork.");
            if (report.spriteRenderers == 0 && report.tilemaps == 0) AddError(report, "Active scene contains no level artwork.");
            if (scene.isDirty) report.warnings = report.warnings.Concat(new[] { "The active scene has unsaved changes." }).ToArray();
        }

        private static void InspectSprite(Sprite sprite, HashSet<string> forbidden)
        {
            if (sprite == null) return;
            string path = AssetDatabase.GetAssetPath(sprite).Replace('\\', '/');
            if (string.IsNullOrEmpty(path) && sprite.texture != null)
                path = AssetDatabase.GetAssetPath(sprite.texture).Replace('\\', '/');
            if (path.StartsWith("Assets/Sprites/", StringComparison.OrdinalIgnoreCase)) forbidden.Add(path);
        }

        private static void AddError(BuildReport report, string error)
        {
            report.errors = report.errors.Concat(new[] { error }).ToArray();
        }

        private static void Complete(BuildReport report)
        {
            report.success = report.errors.Length == 0;
            report.completedUtc = DateTime.UtcNow.ToString("o");
            try
            {
                Directory.CreateDirectory(ArtifactDirectory);
                File.WriteAllText(Path.Combine(ArtifactDirectory, "farm-build-status.json"), JsonUtility.ToJson(report, true));
                if (report.success) Debug.Log("Farm runner completed " + report.operation + ": " + report.scenePath);
                else Debug.LogError("Farm runner failed " + report.operation + ": " + string.Join("\n", report.errors));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                pendingReport = null;
                busy = false;
                nextPoll = EditorApplication.timeSinceStartup + 1.0;
            }
        }
    }
}
