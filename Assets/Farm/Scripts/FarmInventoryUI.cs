using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace Farm
{
    public sealed class FarmInventoryUI : MonoBehaviour
    {
        private FarmGardenSystem garden;
        private RectTransform canvas, popup, toggleRect, shade;
        private Image dragIcon;
        private TMP_Text itemName, description, usage;
        private readonly List<FarmInventorySlotUI> slots=new List<FarmInventorySlotUI>();
        private TMP_FontAsset font;
        private int dragFrom=-1;
        private FarmInventory.Slot dragOriginal;
        public bool IsOpen => popup != null && popup.gameObject.activeSelf;
        public void Initialize(FarmGardenSystem owner,RectTransform parent)
        {
            garden=owner;garden.InventoryUI=this;canvas=parent;
            font=Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            var toggle=ButtonAt(canvas,"Inventory button",new Vector2(218,52),new Vector2(52,56),garden.InventoryArt.Slot,Toggle);
            toggleRect=toggle.GetComponent<RectTransform>();
            var pack=ImageAt(toggleRect,"Backpack",new Vector2(0,7),new Vector2(30,30),garden.InventoryArt.Backpack);
            pack.raycastTarget=false;
            var shortcut=Label(toggleRect,"Shortcut","I",new Vector2(0,-18),new Vector2(40,20),16);
            shortcut.color=Color.white; shortcut.outlineWidth=.15f; shortcut.outlineColor=new Color32(20,30,24,255);
            shade=Rect(canvas,"Inventory backdrop",Vector2.zero,Vector2.zero);
            shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;
            var dark=shade.gameObject.AddComponent<Image>();dark.color=new Color(0,0,0,.25f);
            var backdrop=shade.gameObject.AddComponent<Button>();backdrop.onClick.AddListener(Close);backdrop.navigation=new Navigation{mode=Navigation.Mode.None};
            popup=Rect(canvas,"Inventory window",Vector2.zero,new Vector2(404,384));
            popup.anchorMin=popup.anchorMax=new Vector2(.5f,.5f);
            var panel=popup.gameObject.AddComponent<Image>();panel.sprite=garden.InventoryArt.Panel;panel.type=Image.Type.Sliced;panel.pixelsPerUnitMultiplier=1f/3;
            Label(popup,"Title","INVENTORY",new Vector2(-25,169),new Vector2(260,28),19);
            var close=ButtonAt(popup,"Close inventory",new Vector2(178,169),new Vector2(24,24),garden.InventoryArt.Slot,Close);
            Label(close.transform,"Cross","X",Vector2.zero,new Vector2(24,24),15);
            var slotSprite=garden.InventoryArt.Slot;
            for(int i=0;i<FarmInventory.Capacity;i++)
            {
                var rect=Rect(popup,"Inventory slot "+i,new Vector2(-145+(i%6)*58,107-(i/6)*58),new Vector2(52,52));
                var bg=rect.gameObject.AddComponent<Image>();bg.sprite=slotSprite;bg.type=Image.Type.Sliced;bg.pixelsPerUnitMultiplier=1f/3;
                var icon=ImageAt(rect,"Item",new Vector2(0,3),new Vector2(34,34),null);icon.raycastTarget=false;
                var count=Label(rect,"Count","",new Vector2(6,-15),new Vector2(40,20),13);count.alignment=TextAlignmentOptions.MidlineRight;
                var slot=rect.gameObject.AddComponent<FarmInventorySlotUI>();slot.Owner=this;slot.Index=i;slot.Icon=icon;slot.Count=count;slots.Add(slot);
            }
            itemName=Label(popup,"Item name","",new Vector2(0,-111),new Vector2(350,24),16);
            description=Label(popup,"Item description","",new Vector2(0,-139),new Vector2(354,40),14);
            usage=Label(popup,"Help","",new Vector2(0,-171),new Vector2(355,22),11);
            dragIcon=ImageAt(canvas,"Dragged item",Vector2.zero,new Vector2(36,36),null);dragIcon.raycastTarget=false;dragIcon.gameObject.SetActive(false);
            garden.Inventory.Changed+=Refresh;
            Close();Refresh();
        }
        public void Toggle(){if(IsOpen)Close();else Open();}
        public void Open()
        {
            garden.CancelApproach();garden.SelectTool(GardenTool.None);garden.GetComponent<FarmGardenUI>().CloseMenu();
            shade.gameObject.SetActive(true);popup.gameObject.SetActive(true);ShowItem(-1);Layout();Refresh();
        }
        public void Close()
        {
            CancelDrag();if(popup!=null)popup.gameObject.SetActive(false);if(shade!=null)shade.gameObject.SetActive(false);
        }
        public bool ContainsPointer(Vector2 p) => IsOpen || (toggleRect!=null && RectTransformUtility.RectangleContainsScreenPoint(toggleRect,p));
        private void Layout()
        {
            float scale=Mathf.Min(1,Mathf.Min((Screen.width-24f)/404,(Screen.height-24f)/384));
            popup.localScale=Vector3.one*Mathf.Max(.3f,scale);
        }
        private void LateUpdate()
        {
            if(!IsOpen)return;Layout();
            if(dragFrom>=0 && Mouse.current!=null) dragIcon.rectTransform.position=Mouse.current.position.ReadValue();
        }
        private void Refresh()
        {
            int used=0;
            for(int i=0;i<slots.Count;i++)
            {
                var value=garden.Inventory.GetSlot(i);var item=garden.Inventory.Item(value.Item);
                slots[i].Icon.sprite=item?.Icon;slots[i].Icon.enabled=item!=null;
                slots[i].Count.text=value.Count>0?value.Count.ToString():"";if(value.Count>0)used++;
            }
            if(usage!=null)usage.text=used+" / 24 slots  •  Drag to move  •  I / Esc to close";
        }
        public void ShowItem(int index)
        {
            var item=index<0?null:garden.Inventory.Item(garden.Inventory.GetSlot(index).Item);
            itemName.text=item?.Name??"Your farm supplies";
            description.text=item?.Description??"Hover for details. Plant seeds from the plot menu.";
        }
        public void BeginDrag(int index,Vector2 position)
        {
            var value=garden.Inventory.GetSlot(index);if(value.Count==0)return;
            dragFrom=index;dragOriginal=value;dragIcon.sprite=garden.Inventory.Item(value.Item).Icon;
            dragIcon.gameObject.SetActive(true);dragIcon.rectTransform.position=position;dragIcon.transform.SetAsLastSibling();
        }
        public void EndDrag(int destination)
        {
            if(dragFrom>=0)
            {
                var current=garden.Inventory.GetSlot(dragFrom);
                if(current.Item==dragOriginal.Item && current.Count==dragOriginal.Count)garden.Inventory.Move(dragFrom,destination);
            }
            CancelDrag();
        }
        private void CancelDrag(){dragFrom=-1;if(dragIcon!=null)dragIcon.gameObject.SetActive(false);}
        private RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=parent==canvas?Vector2.zero:new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
        }
        private Image ImageAt(Transform parent,string name,Vector2 pos,Vector2 size,Sprite sprite)
        {var image=Rect(parent,name,pos,size).gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;return image;}
        private TMP_Text Label(Transform parent,string name,string text,Vector2 pos,Vector2 size,int fontSize)
        {
            var value=Rect(parent,name,pos,size).gameObject.AddComponent<TextMeshProUGUI>();value.font=font;value.fontSize=Mathf.Max(14,fontSize);
            value.color=Color.black;value.text=text;value.alignment=TextAlignmentOptions.Center;value.raycastTarget=false;return value;
        }
        private Button ButtonAt(Transform parent,string name,Vector2 pos,Vector2 size,Sprite sprite,UnityEngine.Events.UnityAction action)
        {
            var rect=Rect(parent,name,pos,size);var img=rect.gameObject.AddComponent<Image>();img.sprite=sprite;img.type=Image.Type.Sliced;img.pixelsPerUnitMultiplier=1f/3;
            var button=rect.gameObject.AddComponent<Button>();button.onClick.AddListener(action);button.navigation=new Navigation{mode=Navigation.Mode.None};return button;
        }
        private void OnDestroy(){if(garden?.Inventory!=null)garden.Inventory.Changed-=Refresh;}
    }
    public sealed class FarmInventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public FarmInventoryUI Owner; public int Index;public Image Icon;public TMP_Text Count;
        public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Owner.BeginDrag(Index,e.position);}
        public void OnDrag(PointerEventData e){}
        public void OnEndDrag(PointerEventData e)
        {
            var target=e.pointerCurrentRaycast.gameObject;
            var slot=target==null?null:target.GetComponentInParent<FarmInventorySlotUI>();
            Owner.EndDrag(slot!=null && slot.Owner==Owner?slot.Index:-1);
        }
        public void OnPointerEnter(PointerEventData e)=>Owner.ShowItem(Index);
        public void OnPointerExit(PointerEventData e)=>Owner.ShowItem(-1);
    }
}
