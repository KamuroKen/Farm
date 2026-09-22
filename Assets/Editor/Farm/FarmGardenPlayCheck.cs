using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TMPro;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmGardenPlayCheck
    {
        private const string Armed = "Farm.GardenCheck";
        private static FarmGardenSystem garden;
        private static Mouse mouse;
        private static int step = -1;
        private static float after;
        private static Vector3Int cell;
        private static readonly List<string> checks = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static float previousTimeScale;
        private static Vector2 pointer;
        private static MouseState testMouseState;
        private static void FeedMouse() { if (mouse != null && mouse.added) InputSystem.QueueStateEvent(mouse, testMouseState); }
        private static readonly List<InputDevice> mutedMice = new List<InputDevice>();
        [Serializable] private class Report { public bool success; public string completedUtc; public string[] checks, errors; }

        static FarmGardenPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Armed, false))
                { step = 0; after = Time.time + 0.5f; previousTimeScale = Time.timeScale; checks.Clear(); errors.Clear(); Application.logMessageReceived += OnLog; }
                if (state == PlayModeStateChange.ExitingPlayMode && step >= 0) Finish("Interrupted");
            };
        }

        private static void OnLog(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Library/FarmGardenCheck.request"))
            {
                File.Delete("Library/FarmGardenCheck.request");
                SessionState.SetBool(Armed, true); EditorApplication.EnterPlaymode(); return;
            }
            if (!EditorApplication.isPlaying || step < 0 || Time.time < after) return;
            try
            {
                switch (step)
                {
                    case 0:
                        garden = UnityEngine.Object.FindFirstObjectByType<FarmGardenSystem>();
                        Assert(garden != null && garden.crops.Length == 6, "Six crop definitions installed");
                        Assert(garden.Plots.Count == 0, "A new session starts empty");
                        Assert(garden.crops.All(c => c.stages.Length == 4 && c.stages.All(s => s != null)), "All 24 growth sprites assigned");
                        garden.player.GetComponent<Rigidbody2D>().position = new Vector2(-18, -6);
                        garden.player.position = new Vector3(-18, -6, 0);
                        Physics2D.SyncTransforms();
                        garden.worldCamera.GetComponent<FarmCameraFollow>().SnapToTarget();
                        cell = FindFreeNearPlayer();
                        pointer = garden.worldCamera.WorldToScreenPoint(garden.CellCenter(cell));
                        mutedMice.Clear();
                        foreach (var device in InputSystem.devices.OfType<Mouse>().Where(m => m.enabled).ToArray())
                        { mutedMice.Add(device); InputSystem.DisableDevice(device); }
                        mouse = InputSystem.AddDevice<Mouse>("GardenCheckMouse");
                        mouse.MakeCurrent();
                        InputSystem.onBeforeUpdate += FeedMouse;
                        testMouseState = new MouseState { position = pointer };
                        garden.SelectTool(GardenTool.Place);
                        break;
                    case 1:
                        Assert(garden.PreviewVisible && garden.PreviewValid && garden.HoverCell == cell,
                            "Green preview snaps to target cell: visible=" + garden.PreviewVisible + " valid=" + garden.PreviewValid
                            + " actual=" + garden.HoverCell + " expected=" + cell + " pointer=" + pointer + " read=" + Mouse.current.position.ReadValue()
                            + " hover=" + garden.HoverText + " viewport=" + garden.worldCamera.pixelRect);
                        testMouseState = new MouseState { position = pointer }.WithButton(MouseButton.Left);
                        break;
                    case 2:
                        testMouseState = new MouseState { position = pointer };
                        Assert(garden.Plots.ContainsKey(cell), "Mouse click places light soil");
                        Assert(garden.soil.GetTile(cell) == garden.lightSoil, "Light soil before tilling");
                        Assert(!garden.TryAct(GardenTool.Place, cell), "Duplicate placement rejected");
                        var till = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Tool Till");
                        pointer = RectTransformUtility.WorldToScreenPoint(null, till.GetComponent<RectTransform>().TransformPoint(till.GetComponent<RectTransform>().rect.center));
                        Assert(garden.GetComponent<FarmGardenUI>().ContainsPointer(pointer), "Toolbar blocks world interaction");
                        testMouseState = new MouseState { position = pointer };
                        break;
                    case 3:
                        Assert(!garden.PreviewVisible, "Preview hides over UI");
                        testMouseState = new MouseState { position = pointer }.WithButton(MouseButton.Left);
                        break;
                    case 4:
                        testMouseState = new MouseState { position = pointer };
                        break;
                    case 5:
                        Assert(garden.Plots[cell].tilled && garden.Plots.Count == 1, "Radial Till click tills without placing another plot");
                        Assert(garden.GetComponent<FarmGardenUI>().IsOpen, "Menu stays open after tilling");
                        Assert(!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Tool Till").interactable, "Completed till action is disabled");
                        Assert(!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Tool Harvest").interactable, "Harvest disabled before maturity");
                        var plantButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Tool Plant");
                        plantButton.onClick.Invoke();
                        Assert(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Count(b => b.name.StartsWith("Seed ")) == 6, "Plant opens six seed icons");
                        var seed = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Seed 0");
                        seed.onClick.Invoke();
                        Assert(garden.Plots[cell].crop == 0 && garden.Tool == GardenTool.None, "Seed menu plants into selected plot");
                        garden.TryAct(GardenTool.Water, cell); garden.AdvanceGrowth(15); garden.TryAct(GardenTool.Harvest, cell);
                        Assert(garden.Plots[cell].crop == -1, "Radial plot remains reusable");
                        cell = FindFreeNearPlayer(); garden.TryAct(GardenTool.Place, cell);
                        CheckCycle();
                        PrepareShowcase();
                        break;
                    case 6:
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Gardening-PlayMode.png");
                        break;
                    case 7:
                        var plantMenu = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Tool Plant");
                        // Open seeds on an empty tilled showcase plot.
                        var empty = FindFreeNearPlayer(); garden.TryAct(GardenTool.Place, empty); garden.TryAct(GardenTool.Till, empty);
                        garden.GetComponent<FarmGardenUI>().OpenMenu(empty); plantMenu.onClick.Invoke();
                        break;
                    case 8:
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Gardening-Seeds.png");
                        break;
                    case 9:
                        garden.GetComponent<FarmGardenUI>().CloseMenu();
                        Assert(!garden.GetComponent<FarmGardenUI>().IsOpen, "Menu closes");
                        garden.ToggleBuild(); Assert(garden.Tool == GardenTool.Place, "Build toggles on");
                        garden.ToggleBuild(); Assert(garden.Tool == GardenTool.None, "Build toggles off");
                        var view = garden.worldCamera;
                        var savedPosition = view.transform.position;
                        view.transform.position = garden.CellCenter(cell) + new Vector3(view.orthographicSize * view.aspect - .2f, view.orthographicSize - .2f, savedPosition.z);
                        garden.GetComponent<FarmGardenUI>().OpenMenu(cell);
                        var radial = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(r => r.name == "Plot radial menu");
                        var corners = new Vector3[4]; radial.GetWorldCorners(corners);
                        Assert(corners.All(p => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height), "Radial menu stays on screen near edge");
                        view.transform.position = savedPosition;
                        garden.GetComponent<FarmGardenUI>().CloseMenu();
                        Assert(!UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t => t.text.Any(c => c >= '\u0400' && c <= '\u04ff')), "All visible UI is English");
                        Finish(null);
                        return;
                }
                step++; after = Time.time + 0.3f;
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        private static Vector3Int FindFreeNearPlayer()
        {
            var origin = garden.soil.WorldToCell(garden.player.position);
            var reasons = new List<string>();
            for (int y = 3; y >= -3; y--)
            for (int x = -3; x <= 3; x++)
            {
                var candidate = origin + new Vector3Int(x, y, 0);
                Vector2 screen = garden.worldCamera.WorldToScreenPoint(garden.CellCenter(candidate));
                if (garden.CanAct(GardenTool.Place, candidate, out var reason) && !garden.GetComponent<FarmGardenUI>().ContainsPointer(screen)) return candidate;
                reasons.Add(candidate + ": " + reason);
            }
            throw new Exception("No free test cell near player: " + string.Join("; ", reasons));
        }

        private static void CheckCycle()
        {
            Assert(!garden.TryAct(GardenTool.Plant, cell), "Cannot plant until tilled");
            Assert(!garden.TryAct(GardenTool.Water, cell), "Cannot water raw plot");
            Assert(garden.TryAct(GardenTool.Till, cell) && garden.soil.GetTile(cell) == garden.darkSoil, "Tilling changes soil to dark");
            Assert(!garden.TryAct(GardenTool.Till, cell), "Cannot till twice");
            for (int crop = 0; crop < garden.crops.Length; crop++)
            {
                garden.SelectCrop(crop);
                Assert(garden.TryAct(GardenTool.Plant, cell), "Plant " + garden.crops[crop].name);
                var plot = garden.Plots[cell];
                Assert(plot.stage == 0 && plot.plantRenderer.sprite == garden.crops[crop].stages[0], "Seeds visible immediately " + crop);
                Assert(!garden.TryAct(GardenTool.Plant, cell), "Occupied planting rejected " + crop);
                garden.AdvanceGrowth(65);
                Assert(plot.stage == 0 && plot.growth == 0, "Dry crop does not grow " + crop);
                Assert(!garden.TryAct(GardenTool.Harvest, cell), "Early harvest rejected " + crop);
                Assert(garden.TryAct(GardenTool.Water, cell) && plot.waterRenderer.enabled, "Water marker visible " + crop);
                Assert(!garden.TryAct(GardenTool.Water, cell), "Repeated watering rejected " + crop);
                for (int stage = 1; stage <= 3; stage++)
                {
                    garden.AdvanceGrowth(5);
                    Assert(plot.stage == stage && plot.plantRenderer.sprite == garden.crops[crop].stages[stage], "Growth stage " + stage + " for " + crop);
                }
                Assert(garden.TryAct(GardenTool.Harvest, cell) && garden.HarvestCount(crop) == (crop == 0 ? 2 : 1), "Harvest counted " + crop);
                Assert(plot.tilled && plot.crop == -1 && !plot.watered && !plot.plantRenderer.enabled, "Plot reusable after harvest " + crop);
                Assert(!garden.TryAct(GardenTool.Harvest, cell) && garden.HarvestCount(crop) == (crop == 0 ? 2 : 1), "Cannot collect twice " + crop);
            }
            Assert(garden.TryAct(GardenTool.Water, cell), "Can water before planting");
            garden.SelectCrop(0); garden.TryAct(GardenTool.Plant, cell); garden.AdvanceGrowth(5);
            Assert(garden.Plots[cell].stage == 1, "Pre-watered planting grows");
            Assert(!garden.CanPlaceAt(new Vector3Int(50, 50, 0), out _), "Outside map rejected");
            bool water = false, path = false, scenery = false;
            for (int x = -30; x < 30; x++)
            for (int y = -22; y < 22; y++)
            {
                garden.CanPlaceAt(new Vector3Int(x, y, 0), out string reason);
                water |= reason.Contains("water"); path |= reason.Contains("paths"); scenery |= reason.Contains("Blocked by a plant");
                if (water && path && scenery) break;
            }
            Assert(water && path && scenery, "Water, paths and decorative plants block placement");
            Assert(!garden.CanAct(GardenTool.Place, new Vector3Int(30, 20, 0), out _), "Interaction distance enforced");
        }

        private static void PrepareShowcase()
        {
            // Use the cleared old garden to show all four phases in the real Game view.
            var body = garden.player.GetComponent<Rigidbody2D>();
            body.position = new Vector2(-18.5f, -10.5f);
            garden.player.position = new Vector3(-18.5f, -10.5f, 0);
            Physics2D.SyncTransforms();
            var follow = garden.worldCamera.GetComponent<FarmCameraFollow>(); follow.SnapToTarget();
            var cells = new List<Vector3Int>();
            for (int y = -9; y <= -7; y++)
            for (int x = -21; x <= -17; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (garden.CanAct(GardenTool.Place, candidate, out _)) cells.Add(candidate);
            }
            Assert(cells.Count >= 4, "Free cells available in former decorative garden");
            for (int i = 0; i < Mathf.Min(cells.Count, 12); i++)
            {
                garden.TryAct(GardenTool.Place, cells[i]); garden.TryAct(GardenTool.Till, cells[i]);
                garden.SelectCrop(i % 6); garden.TryAct(GardenTool.Plant, cells[i]);
                if (i % 4 > 0)
                {
                    garden.TryAct(GardenTool.Water, cells[i]);
                    var plot = garden.Plots[cells[i]];
                    plot.growth = (i % 4) * 5 - 0.01f;
                }
            }
            garden.AdvanceGrowth(0.02f);
            garden.SelectTool(GardenTool.None);
            garden.GetComponent<FarmGardenUI>().OpenMenu(cells[0]);
            var previewCell = cells.Last() + Vector3Int.right;
            testMouseState = new MouseState { position = garden.worldCamera.WorldToScreenPoint(garden.CellCenter(previewCell)) };
        }

        private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add(message); }

        private static void Finish(string error)
        {
            if (error != null) errors.Add(error);
            step = -1;
            Application.logMessageReceived -= OnLog;
            InputSystem.onBeforeUpdate -= FeedMouse;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            foreach (var device in mutedMice) if (device.added) InputSystem.EnableDevice(device);
            mutedMice.Clear();
            Time.timeScale = previousTimeScale;
            SessionState.SetBool(Armed, false);
            Directory.CreateDirectory("BuildArtifacts");
            File.WriteAllText("BuildArtifacts/garden-play-check.json", JsonUtility.ToJson(new Report {
                success = errors.Count == 0, completedUtc = DateTime.UtcNow.ToString("o"), checks = checks.ToArray(), errors = errors.ToArray()
            }, true));
            EditorApplication.ExitPlaymode();
        }
    }
}
