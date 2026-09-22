using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace Farm
{
    [RequireComponent(typeof(FarmGardenSystem))]
    public sealed class FarmGardenUI : MonoBehaviour
    {
        private FarmGardenSystem garden;
        private TMP_FontAsset font;
        private RectTransform canvasRect, menu, actions, seeds, buildRect;
        private TMP_Text hint, caption, tooltip;
        private Image buildImage;
        private Sprite disc;
        private SpriteRenderer selection;
        private Vector3Int selectedCell;
        private bool seedMenu;
        private FarmClearable clearTarget;
        private Button clearButton;
        private readonly List<Button> actionButtons = new List<Button>();
        private readonly List<Button> seedButtons = new List<Button>();
        private readonly List<Sprite> generated = new List<Sprite>();
        private static readonly Color Ink = Color.white;
        private static readonly Color Surface = new Color32(32, 48, 39, 245);
        public bool IsOpen => menu != null && menu.gameObject.activeSelf;
        public static string ToolName(GardenTool tool) => tool == GardenTool.Place ? "Build" : tool == GardenTool.None ? "" : tool.ToString();

        private void Start()
        {
            garden = GetComponent<FarmGardenSystem>();
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            var go = new GameObject("Garden UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            go.GetComponent<Canvas>().sortingOrder = 50;
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasRect = go.GetComponent<RectTransform>();
            go.GetComponent<Canvas>().pixelPerfect = true;
            if (EventSystem.current == null)
            {
                var events = new GameObject("Garden Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var marker = new GameObject("Selected plot"); marker.transform.SetParent(transform, false);
            selection = marker.AddComponent<SpriteRenderer>(); selection.sprite = garden.cursorSprite;
            selection.sharedMaterial = garden.spriteMaterial; selection.sortingOrder = 90;
            selection.color = new Color(1f, .88f, .5f, .75f); selection.enabled = false;
            disc = DrawIcon("Disc");
            var build = MakeButton(canvasRect, "Build", new Vector2(102, 52), DrawIcon("Build"), garden.ToggleBuild);
            buildRect = build.GetComponent<RectTransform>(); buildImage = build.GetComponent<Image>();
            buildRect.sizeDelta = new Vector2(156, 56);
            buildImage.sprite = null;
            var border = build.gameObject.AddComponent<Outline>();
            border.effectColor = new Color32(211, 193, 137, 255); border.effectDistance = new Vector2(1, -1);
            var buildIcon = buildRect.Find("Icon").GetComponent<RectTransform>();
            buildIcon.anchoredPosition = new Vector2(-53, 0); buildIcon.sizeDelta = new Vector2(30, 30);
            Label(buildRect, "Build label", "Build", new Vector2(-5, 0), new Vector2(60, 30), 17);
            var key = Rect(buildRect, "Shortcut key", new Vector2(52, 0), new Vector2(26, 28));
            key.gameObject.AddComponent<Image>().color = new Color32(52, 70, 55, 255);
            var keyLabel = Label(key, "Build shortcut", "B", Vector2.zero, new Vector2(26, 28), 18);
            keyLabel.color = Color.white;

            hint = Label(canvasRect, "Build hint", "", new Vector2(164, 106), new Vector2(280, 34), 12);
            menu = Rect(canvasRect, "Plot radial menu", Vector2.zero, new Vector2(224, 220));
            actions = Rect(menu, "Actions", Vector2.zero, Vector2.zero);
            seeds = Rect(menu, "Seeds", Vector2.zero, Vector2.zero);
            var types = new[] { GardenTool.Till, GardenTool.Plant, GardenTool.Water, GardenTool.Harvest };
            for (int i = 0; i < types.Length; i++)
            {
                var tool = types[i];
                float angle = (135 - i * 90) * Mathf.Deg2Rad;
                var button = MakeButton(actions, "Tool " + tool, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 70,
                    tool == GardenTool.Plant ? garden.crops[0].seedPacket : DrawIcon(tool.ToString()), () => Act(tool));
                actionButtons.Add(button);
            }
            for (int i = 0; i < garden.crops.Length; i++)
            {
                int crop = i;
                float angle = (90 - i * 360f / garden.crops.Length) * Mathf.Deg2Rad;
                seedButtons.Add(MakeButton(seeds, "Seed " + i, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 72,
                    garden.crops[i].seedPacket, () => Plant(crop)));
                Label(seedButtons[i].transform,"Seed count","",new Vector2(12,-12),new Vector2(24,18),12);
            }
            MakeButton(seeds, "Back", Vector2.zero, DrawIcon("Back"), () => { seedMenu = false; Refresh(); });
            clearButton = MakeButton(menu, "Clear", new Vector2(0, 65), DrawIcon("Scythe"), () => garden.TryClear(clearTarget));
            caption = Label(menu, "Plot status", "", new Vector2(0, -112), new Vector2(260, 20), 12);
            tooltip = Label(menu, "Tooltip", "", new Vector2(0, 111), new Vector2(280, 34), 12);
            CloseMenu();
            gameObject.AddComponent<FarmInventoryUI>().Initialize(garden,canvasRect);
        }

        public void OpenMenu(Vector3Int cell)
        {
            if (menu == null || !garden.Plots.ContainsKey(cell)) return;
            clearTarget = null; selection.transform.localScale = Vector3.one;
            selectedCell = cell; seedMenu = false;
            garden.SelectTool(GardenTool.None);
            menu.gameObject.SetActive(true);
            selection.transform.position = garden.CellCenter(cell); selection.enabled = true;
            Refresh();
        }
        public void OpenClearMenu(FarmClearable target)
        {
            if (menu == null || target == null || !target.Available) return;
            clearTarget = target; seedMenu = false;
            garden.SelectTool(GardenTool.None);
            menu.gameObject.SetActive(true);
            selection.transform.position = target.Center;
            var size = target.VisualBounds.size;
            selection.transform.localScale = new Vector3(Mathf.Max(.5f,size.x), Mathf.Max(.5f,size.y),1);
            selection.enabled = true;
            Refresh();
        }
        public void CloseMenu() { if (menu != null) menu.gameObject.SetActive(false); if (selection != null) selection.enabled = false; }
        private void Act(GardenTool tool)
        {
            if (!garden.CanAct(tool, selectedCell, out _)) return;
            if (tool == GardenTool.Plant) seedMenu = true;
            else garden.TryAct(tool, selectedCell);
            Refresh();
        }
        private void Plant(int crop)
        {
            garden.SelectCrop(crop);
            garden.TryAct(GardenTool.Plant, selectedCell);
            seedMenu = false;
            Refresh();
        }
        private void LateUpdate()
        {
            if (garden == null) return;
            buildImage.color = garden.Tool == GardenTool.Place ? new Color32(129, 104, 53, 255) : Surface;
            hint.text = garden.Tool == GardenTool.Place ? (string.IsNullOrEmpty(garden.HoverText) ? "Choose a free cell • Esc to cancel" : garden.HoverText) : garden.Feedback;
            if (IsOpen) Refresh();
        }
        private void Refresh()
        {
            if (!IsOpen) return;
            if (clearTarget != null && !clearTarget.Available) { CloseMenu(); return; }
            bool clearing = clearTarget != null;
            Vector3 screen = garden.worldCamera.WorldToScreenPoint(clearing ? (Vector3)clearTarget.Center : garden.CellCenter(selectedCell));
            if (screen.z < 0 || !garden.worldCamera.pixelRect.Contains((Vector2)screen)) { CloseMenu(); return; }
            menu.anchoredPosition = new Vector2(Mathf.Clamp(screen.x, 145, Mathf.Max(145, Screen.width - 145)),
                Mathf.Clamp(screen.y, 135, Mathf.Max(135, Screen.height - 135)));
            menu.anchoredPosition = new Vector2(Mathf.Round(menu.anchoredPosition.x), Mathf.Round(menu.anchoredPosition.y));
            clearButton.gameObject.SetActive(clearing);
            actions.gameObject.SetActive(!clearing && !seedMenu); seeds.gameObject.SetActive(!clearing && seedMenu);
            if (clearing)
            {
                bool room=garden.Inventory.CanAdd(clearTarget.Loot(garden));
                SetAvailable(clearButton, !garden.IsBusy && room);
                caption.text = clearTarget.name;
                tooltip.text = garden.IsBusy ? "Clearing..." : room ? "Clear" : "Inventory full";
                return;
            }
            caption.text = garden.Describe(selectedCell);
            tooltip.text = seedMenu ? "Choose seeds" : "";
            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            for (int i = 0; i < actionButtons.Count; i++)
            {
                GardenTool tool = (GardenTool)(i + 2);
                bool enabled = garden.CanAct(tool, selectedCell, out string reason);
                SetAvailable(actionButtons[i], enabled);
                if (!seedMenu && Inside(actionButtons[i].GetComponent<RectTransform>(), pointer))
                    tooltip.text = ToolName(tool) + (enabled ? "" : "\n" + reason);
            }
            bool canPlant = garden.CanAct(GardenTool.Plant, selectedCell, out string plantReason);
            for (int i = 0; i < seedButtons.Count; i++)
            {
                int quantity=garden.Inventory.Count(garden.SeedId(i));
                seedButtons[i].transform.Find("Seed count").GetComponent<TMP_Text>().text=quantity.ToString();
                SetAvailable(seedButtons[i], canPlant && quantity>0);
                if (seedMenu && Inside(seedButtons[i].GetComponent<RectTransform>(), pointer))
                    tooltip.text = garden.crops[i].displayName + (quantity==0 ? "\nNo seeds" : canPlant ? "" : "\n" + plantReason);
            }
        }
        private static void SetAvailable(Button button, bool available)
        {
            button.interactable = available;
            button.transform.Find("Icon").GetComponent<Image>().color = available ? Color.white : new Color(.55f, .55f, .55f, .45f);
        }
        private static bool Inside(RectTransform rect, Vector2 point) => RectTransformUtility.RectangleContainsScreenPoint(rect, point);
        public bool ContainsPointer(Vector2 position) => (buildRect != null && Inside(buildRect, position)) || (IsOpen && Inside(menu, position));

        private RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = parent == canvasRect ? Vector2.zero : new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private TMP_Text Label(Transform parent, string name, string text, Vector2 position, Vector2 size, int fontSize)
        {
            var label = Rect(parent, name, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = Ink;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; label.fontSize = Mathf.Max(14, fontSize); label.textWrappingMode = TextWrappingModes.Normal;
            label.outlineWidth = .15f; label.outlineColor = new Color32(20,30,24,255);

            return label;
        }
        private Button MakeButton(Transform parent, string name, Vector2 position, Sprite sprite, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(parent, name, position, new Vector2(42, 42));
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = disc; image.color = Surface;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.5f, 1.5f, 1.3f); colors.disabledColor = new Color(.7f, .7f, .7f, .7f); button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(action);
            var icon = Rect(rect, "Icon", Vector2.zero, new Vector2(26, 26)).gameObject.AddComponent<Image>();
            icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            return button;
        }
        // Small pixel icons match the existing artwork and require no font symbols.
        private Sprite DrawIcon(string kind)
        {
            var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Garden " + kind };
            var pixels = new Color[24 * 24];
            void Box(int x, int y, int w, int h, Color color) { for (int a = x; a < x+w; a++) for (int b = y; b < y+h; b++) if (a>=0 && a<24 && b>=0 && b<24) pixels[b*24+a] = color; }
            Color wood = new Color32(185, 129, 69, 255), metal = new Color32(200, 220, 210, 255), green = new Color32(145, 185, 83, 255);
            if (kind == "Disc") { for(int y=0;y<24;y++) for(int x=0;x<24;x++) if (Vector2.Distance(new Vector2(x,y), new Vector2(11.5f,11.5f)) < 12) pixels[y*24+x] = Color.white; }
            if (kind == "Build") { Box(3,5,18,13,wood); for(int i=0;i<3;i++) Box(5,7+i*4,14,2,new Color32(105,68,44,255)); Box(16,14,2,9,metal); Box(13,17,8,2,metal); }
            if (kind == "Till") { Box(10,3,3,17,wood); Box(5,17,14,4,metal); Box(5,14,4,6,metal); }
            if (kind == "Scythe") { Box(7,2,3,18,wood); Box(8,18,9,3,metal); Box(16,16,4,3,metal); Box(19,13,2,4,metal); }
            if (kind == "Water") { Box(5,5,12,11,new Color32(95,177,209,255)); Box(16,12,5,3,metal); Box(19,14,3,5,metal); Box(2,10,3,8,metal); Box(3,17,9,2,metal); Box(9,16,3,3,metal); }
            if (kind == "Harvest") { Box(5,4,15,10,wood); Box(7,15,3,4,green); Box(13,14,5,6,green); Box(3,13,19,2,metal); Box(7,7,2,4,metal); Box(12,7,2,4,metal); Box(17,7,2,4,metal); }
            if (kind == "Back") { Box(6,10,15,3,metal); for(int i=0;i<6;i++) Box(4+i,11-i,3,2,metal); for(int i=0;i<6;i++) Box(4+i,11+i,3,2,metal); }
            texture.SetPixels(pixels); texture.Apply();
            var result = Sprite.Create(texture, new Rect(0,0,24,24), new Vector2(.5f,.5f),24);
            generated.Add(result); return result;
        }
        private void OnDestroy() { foreach(var sprite in generated) { if(sprite != null) { Destroy(sprite.texture); Destroy(sprite); } } }
    }
}

