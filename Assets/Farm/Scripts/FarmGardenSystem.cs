using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace Farm
{
    public enum GardenTool { None, Place, Till, Plant, Water, Harvest }

    public sealed class FarmGardenSystem : MonoBehaviour
    {
        public Camera worldCamera;
        public Transform player;
        public Tilemap ground;
        public Tilemap[] forbiddenSurfaces;
        public Tilemap soil;
        public TileBase lightSoil;
        public TileBase darkSoil;
        public Sprite cursorSprite;
        public Sprite waterMarker;
        public Material spriteMaterial;
        public Transform environment;
        public FarmCropDefinition[] crops;
        [Min(1)] public float interactionDistance = 4.5f;

        public sealed class Plot
        {
            public bool tilled, watered;
            public int crop = -1;
            public float growth;
            public int stage;
            public SpriteRenderer plantRenderer, waterRenderer;
        }

        private readonly Dictionary<Vector3Int, Plot> plots = new Dictionary<Vector3Int, Plot>();
        private SpriteRenderer[] scenery;
        private SpriteRenderer preview;
        private int[] harvestCounts;
        private FarmGardenUI ui;
        private float feedbackUntil;
        public bool IsBusy { get; private set; }
        private FarmPlayerController worker;
        private FarmPlotPathfinder pathfinder;
        public bool IsApproaching => worker != null && worker.IsWalkingToPlot;
        private int commandVersion;
        public GardenTool Tool { get; private set; } = GardenTool.None;
        public int SelectedCrop { get; private set; }
        public string Feedback { get; private set; } = "";
        public string HoverText { get; private set; } = "";
        public Vector3Int HoverCell { get; private set; }
        public bool PreviewVisible => preview != null && preview.enabled;
        public bool PreviewValid { get; private set; }
        public IReadOnlyDictionary<Vector3Int, Plot> Plots => plots;
        public int HarvestCount(int crop) => harvestCounts[crop];
        public event Action Changed;

        private void Awake()
        {
            scenery = environment.GetComponentsInChildren<SpriteRenderer>(true);
            harvestCounts = new int[crops.Length];
            preview = MakeSprite("Placement preview", cursorSprite, 90);
            preview.enabled = false;
            ui = GetComponent<FarmGardenUI>();
            worker = player.GetComponent<FarmPlayerController>();
            pathfinder = new FarmPlotPathfinder(ground, player);
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = order;
            return renderer;
        }

        public Vector3 CellCenter(Vector3Int cell) => soil.GetCellCenterWorld(cell);

        public void SelectTool(GardenTool tool)
        {
            Tool = tool;
            SetFeedback(tool == GardenTool.None ? "" : "Selected: " + FarmGardenUI.ToolName(tool));
        }

        public void SelectCrop(int index)
        {
            if (index < 0 || index >= crops.Length) return;
            SelectedCrop = index;
            Tool = GardenTool.Plant;
            SetFeedback("Seeds: " + crops[index].displayName + ". Choose a tilled plot");
        }

        private void Update()
        {
            AdvanceGrowth(Time.deltaTime);
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame) ToggleBuild();
            var mouse = Mouse.current;
            if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
            { CancelApproach(); SelectTool(GardenTool.None); ui.CloseMenu(); }
            if (mouse == null || worldCamera == null) { preview.enabled = false; return; }
            Vector2 pointer = mouse.position.ReadValue();
            bool overUI = ui != null && ui.ContainsPointer(pointer);
            preview.enabled = false;
            HoverText = "";
            if (overUI || !worldCamera.pixelRect.Contains(pointer)) return;
            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, -worldCamera.transform.position.z));
            HoverCell = soil.WorldToCell(world);
            HoverCell = new Vector3Int(HoverCell.x, HoverCell.y, 0);
            if (Tool == GardenTool.Place)
            {
                PreviewValid = CanAct(Tool, HoverCell, out string reason);
                preview.enabled = true;
                preview.transform.position = CellCenter(HoverCell);
                preview.color = PreviewValid ? new Color(0.45f, 1f, 0.55f, 0.65f) : new Color(1f, 0.28f, 0.23f, 0.65f);
                HoverText = PreviewValid ? "Click to build a plot" : reason;
                if (mouse.leftButton.wasPressedThisFrame && TryAct(Tool, HoverCell))
                { SelectTool(GardenTool.None); ui.OpenMenu(HoverCell); }
            }
            else if (mouse.leftButton.wasPressedThisFrame)
            {
                if (plots.ContainsKey(HoverCell)) ui.OpenMenu(HoverCell);
                else ui.CloseMenu();
            }
            if (Time.unscaledTime > feedbackUntil) Feedback = "";
        }

        public void ToggleBuild()
        {
            CancelApproach();
            ui.CloseMenu();
            SelectTool(Tool == GardenTool.Place ? GardenTool.None : GardenTool.Place);
        }

        public bool CanAct(GardenTool tool, Vector3Int cell, out string reason)
        {
            reason = "";
            if (IsBusy) { reason = "Working..."; return false; }
            if (tool == GardenTool.None) { reason = "Choose an action"; return false; }
            Vector3 center = CellCenter(cell);
            if (tool == GardenTool.Place && Vector2.Distance(player.position, center) > interactionDistance)
            { reason = "Move closer"; return false; }
            bool exists = plots.TryGetValue(cell, out Plot plot);
            if (tool == GardenTool.Place)
            {
                if (exists) { reason = "A plot is already here"; return false; }
                return CanPlaceAt(cell, out reason);
            }
            if (!exists) { reason = "Build a plot first"; return false; }
            switch (tool)
            {
                case GardenTool.Till:
                    if (plot.tilled) { reason = "Already tilled"; return false; }
                    break;
                case GardenTool.Plant:
                    if (!plot.tilled) { reason = "Till the soil first"; return false; }
                    if (plot.crop >= 0) { reason = "A crop is already growing"; return false; }
                    break;
                case GardenTool.Water:
                    if (!plot.tilled) { reason = "Till the soil first"; return false; }
                    if (plot.watered) { reason = "Already watered"; return false; }
                    break;
                case GardenTool.Harvest:
                    if (plot.crop < 0) { reason = "Nothing planted yet"; return false; }
                    if (plot.stage < 3) { reason = "Not ready to harvest"; return false; }
                    break;
            }
            return true;
        }

        public bool CanPlaceAt(Vector3Int cell, out string reason)
        {
            reason = "";
            Vector3 center = CellCenter(cell);
            if (plots.ContainsKey(cell)) { reason = "A plot is already here"; return false; }
            // Check the whole footprint, including all four half-size terrain tiles.
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                Vector3 point = center + new Vector3(x * 0.44f, y * 0.44f, 0);
                Sprite surface = ground.GetSprite(ground.WorldToCell(point));
                if (surface == null || surface.name != "ground_grass")
                { reason = "Build on grass, away from water"; return false; }
                foreach (var map in forbiddenSurfaces)
                    if (map != null && map.gameObject.activeInHierarchy && map.HasTile(map.WorldToCell(point)))
                    { reason = "Keep paths and shores clear"; return false; }
            }
            Bounds footprint = new Bounds(center, new Vector3(0.88f, 0.88f, 5));
            foreach (var renderer in scenery)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null) continue;
                if (footprint.Intersects(renderer.bounds))
                { reason = "Blocked by a plant or object"; return false; }
            }
            foreach (var collider in Physics2D.OverlapBoxAll(center, Vector2.one * 0.88f, 0))
                if (!collider.isTrigger)
                { reason = collider.GetComponentInParent<FarmPlayerController>() != null ? "Step away from this cell" : "Blocked by an obstacle"; return false; }
            return true;
        }

        public bool TryAct(GardenTool tool, Vector3Int cell)
        {
            if (!CanAct(tool, cell, out string reason)) { SetFeedback(reason); return false; }
            CancelApproach();
            if (tool != GardenTool.Place && !pathfinder.CanWorkFrom(player.position, CellCenter(cell)))
            {
                if (!pathfinder.Find(player.position, CellCenter(cell), out var route))
                { SetFeedback("Can't reach this plot"); return false; }
                int version = commandVersion;
                int crop = SelectedCrop;
                worker.WalkToPlot(route, pathfinder.ClearSegment, reached => {
                    if (version != commandVersion) return;
                    if (!reached) { SetFeedback("Command cancelled or path blocked"); return; }
                    if (!isActiveAndEnabled || !pathfinder.CanWorkFrom(player.position, CellCenter(cell)))
                    { SetFeedback("Can't reach this plot"); return; }
                    SelectedCrop = crop;
                    if (CanAct(tool, cell, out string why)) ExecuteAction(tool, cell);
                    else SetFeedback(why);
                });
                SetFeedback("Walking to plot...");
                return true;
            }
            return ExecuteAction(tool, cell);
        }

        public void CancelApproach()
        {
            commandVersion++;
            if (worker != null) worker.CancelWalk();
        }

        private bool ExecuteAction(GardenTool tool, Vector3Int cell)
        {
            if (tool == GardenTool.Till || tool == GardenTool.Water)
            {
                IsBusy = true;
                bool started = worker != null && worker.BeginWork(tool == GardenTool.Till ? "Scythe" : "Watering", CellCenter(cell), completed => {
                    IsBusy = false;
                    if (completed && isActiveAndEnabled && pathfinder.CanWorkFrom(player.position, CellCenter(cell)) && CanAct(tool, cell, out _)) ApplyAction(tool, cell);
                    Changed?.Invoke();
                });
                if (!started) { IsBusy = false; SetFeedback("Action animation unavailable"); return false; }
                SetFeedback("Working...");
                return true;
            }
            return ApplyAction(tool, cell);
        }

        private bool ApplyAction(GardenTool tool, Vector3Int cell)
        {
            if (tool == GardenTool.Place)
            {
                plots.Add(cell, new Plot());
                soil.SetTile(cell, lightSoil);
                SetFeedback("Plot built. Till the soil");
                return true;
            }
            Plot plot = plots[cell];
            switch (tool)
            {
                case GardenTool.Till:
                    plot.tilled = true;
                    soil.SetTile(cell, darkSoil);
                    SetFeedback("Soil ready for planting");
                    break;
                case GardenTool.Plant:
                    plot.crop = SelectedCrop; plot.growth = 0; plot.stage = 0;
                    if (plot.plantRenderer == null) plot.plantRenderer = MakeSprite("Crop " + cell, null, 25);
                    plot.plantRenderer.transform.position = CellCenter(cell);
                    plot.plantRenderer.sprite = crops[plot.crop].stages[0];
                    plot.plantRenderer.enabled = true;
                    SetFeedback(crops[plot.crop].displayName + ": planted" + (plot.watered ? " — growing" : ". Needs water"));
                    break;
                case GardenTool.Water:
                    plot.watered = true;
                    if (plot.waterRenderer == null) plot.waterRenderer = MakeSprite("Water " + cell, waterMarker, 26);
                    plot.waterRenderer.transform.localScale = Vector3.one * 0.55f;
                    plot.waterRenderer.transform.position = CellCenter(cell) + new Vector3(0.32f, -0.3f, 0);
                    plot.waterRenderer.enabled = true;
                    SetFeedback("Watered");
                    break;
                case GardenTool.Harvest:
                    int index = plot.crop;
                    harvestCounts[index] += crops[index].harvestAmount;
                    plot.crop = -1; plot.growth = 0; plot.stage = 0; plot.watered = false;
                    plot.plantRenderer.enabled = false;
                    if (plot.waterRenderer != null) plot.waterRenderer.enabled = false;
                    SetFeedback("+" + crops[index].harvestAmount + " " + crops[index].displayName + ". Ready to replant");
                    break;
            }
            Changed?.Invoke();
            return true;
        }

        public void AdvanceGrowth(float seconds)
        {
            if (seconds <= 0) return;
            foreach (Plot plot in plots.Values)
            {
                if (!plot.watered || plot.crop < 0 || plot.stage == 3) continue;
                var crop = crops[plot.crop];
                plot.growth = Mathf.Min(crop.growthSeconds, plot.growth + seconds);
                int stage = Mathf.Min(3, Mathf.FloorToInt(plot.growth / (crop.growthSeconds / 3f)));
                if (stage == plot.stage) continue;
                plot.stage = stage;
                plot.plantRenderer.sprite = crop.stages[stage];
                Changed?.Invoke();
            }
        }

        public string Describe(Vector3Int cell)
        {
            if (!plots.TryGetValue(cell, out Plot plot)) return "Empty ground";
            if (!plot.tilled) return "Untilled soil";
            if (plot.crop < 0) return plot.watered ? "Wet soil • Ready to plant" : "Tilled soil • Ready to plant";
            string[] phases = { "Seeds", "Sprout", "Growing", "Ready to harvest" };
            string text = crops[plot.crop].displayName + " · " + phases[plot.stage];
            if (plot.stage < 3) text += plot.watered ? " · " + Mathf.CeilToInt(crops[plot.crop].growthSeconds - plot.growth) + " s" : " • Needs water";
            return text;
        }

        private void SetFeedback(string text)
        {
            Feedback = text;
            feedbackUntil = Time.unscaledTime + 3;
            Changed?.Invoke();
        }

        private void OnDisable() { CancelApproach(); if (worker != null && IsBusy) worker.CancelWork(); IsBusy = false; if (preview != null) preview.enabled = false; }
    }
}
