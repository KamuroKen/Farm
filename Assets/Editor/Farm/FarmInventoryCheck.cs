using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmInventoryCheck
    {
        private static int step=-1,checks;
        private static Keyboard keyboard;
        private static KeyboardState keyState;
        private static void FeedKey(){if(keyboard!=null&&keyboard.added)InputSystem.QueueStateEvent(keyboard,keyState);}
        private static float after,deadline;
        private static FarmGardenSystem garden;
        private static FarmClearable target;
        private static Vector3Int cell=new Vector3Int(-19,-8,0);
        private static Dictionary<string,int> loot,before;
        private static readonly List<string> errors=new List<string>();
        static FarmInventoryCheck()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Farm.InventoryCheck",false))Begin();};
            EditorApplication.delayCall+=()=>{if(EditorApplication.isPlaying && SessionState.GetBool("Farm.InventoryCheck",false)&&step<0)Begin();};
        }
        private static void Begin(){File.WriteAllText("BuildArtifacts/inventory-progress.txt","Started");step=0;checks=0;errors.Clear();after=Time.time+.5f;deadline=Time.time+45;Application.logMessageReceived+=Log;}
        private static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text);}
        private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        private static void Move(Vector2 p){garden.player.position=p;garden.player.GetComponent<Rigidbody2D>().position=p;Physics2D.SyncTransforms();}
        private static void Fill(){while(garden.Inventory.TryAdd("stone",99)){} }
        private static void ModelChecks()
        {
            var model=new FarmInventory();model.Register(new FarmItem("a","A","",null));model.Register(new FarmItem("b","B","",null));
            Check(model.TryAdd("a",100),"Add split stacks");Check(model.GetSlot(0).Count==99&&model.GetSlot(1).Count==1,"Stack limit");
            Check(model.TryRemove("a",3),"Remove");model.Move(1,0);Check(model.Count("a")==97&&model.GetSlot(0).Count==97,"Merge preserves amount");
            model.TryAdd("b",2);model.Move(0,1);Check(model.GetSlot(0).Item=="b"&&model.GetSlot(1).Item=="a","Swap stacks");
            model.Move(1,23);Check(model.GetSlot(23).Count==97,"Move to empty slot");model.Move(23,-1);Check(model.GetSlot(23).Count==97,"Outside drop preserves stack");
            Check(!model.TryRemove("a",98)&&model.Count("a")==97,"Insufficient removal atomic");
            while(model.TryAdd("b",99)){}
            int saved=model.Count("b");Check(!model.TryAdd(new Dictionary<string,int>{{"a",1},{"b",9999}})&&model.Count("a")==97&&model.Count("b")==saved,"Multi-item reward atomic");
        }
        private static void Finish(string error)
        {
            InputSystem.onBeforeUpdate-=FeedKey;if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            Application.logMessageReceived-=Log;
            File.WriteAllText("BuildArtifacts/inventory-check.txt",error??(errors.Count==0?"PASS: "+checks+" inventory checks":string.Join("\n",errors)));
            step=-1;SessionState.SetBool("Farm.InventoryCheck",false);EditorApplication.ExitPlaymode();
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists("Library/FarmInventoryCheck.request"))
            {File.Delete("Library/FarmInventoryCheck.request");SessionState.SetBool("Farm.InventoryCheck",true);EditorApplication.EnterPlaymode();return;}
            if(EditorApplication.isPlaying && step<0 && SessionState.GetBool("Farm.InventoryCheck",false))Begin();
            if(!EditorApplication.isPlaying||step<0||Time.time<after)return;
            try
            {
                if(Time.time>deadline)throw new Exception("Timeout at step "+step);
                File.WriteAllText("BuildArtifacts/inventory-progress.txt","Step "+step+" time "+Time.time);
                switch(step)
                {
                    case 0:
                        ModelChecks();garden=UnityEngine.Object.FindFirstObjectByType<FarmGardenSystem>();
                        for(int i=0;i<6;i++)Check(garden.Inventory.Count(garden.SeedId(i))==5,"Five starter seeds "+i);
                        Check(garden.InventoryUI!=null,"Inventory UI installed");
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down*2);
                        Check(garden.TryAct(GardenTool.Place,cell),"Build");Check(garden.TryAct(GardenTool.Till,cell),"Till");
                        break;
                    case 1:
                        if(garden.IsApproaching||garden.IsBusy)return;
                        Check(garden.Plots[cell].tilled,"Tilled");
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down*3);garden.SelectCrop(0);
                        Check(garden.TryAct(GardenTool.Plant,cell),"Queue planting");garden.CancelApproach();
                        Check(garden.Inventory.Count(garden.SeedId(0))==5,"Cancel does not consume seed");
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down);Check(garden.TryAct(GardenTool.Plant,cell),"Plant");
                        Check(garden.Inventory.Count(garden.SeedId(0))==4&&garden.Plots[cell].crop==0,"Plant consumes one seed");
                        Check(!garden.TryAct(GardenTool.Plant,cell)&&garden.Inventory.Count(garden.SeedId(0))==4,"Duplicate plant consumes nothing");
                        Check(garden.TryAct(GardenTool.Water,cell),"Water");break;
                    case 2:
                        if(garden.IsBusy)return;garden.AdvanceGrowth(15);Fill();
                        Check(!garden.TryAct(GardenTool.Harvest,cell)&&garden.Plots[cell].stage==3,"Full inventory preserves ripe plant");
                        garden.Inventory.TryRemove("stone",99);
                        Check(garden.TryAct(GardenTool.Harvest,cell)&&garden.Inventory.Count(garden.ProduceId(0))==1,"Harvest adds produce");
                        Check(!garden.TryAct(GardenTool.Harvest,cell)&&garden.Inventory.Count(garden.ProduceId(0))==1,"No duplicate harvest");
                        garden.Inventory.TryRemove(garden.SeedId(0),4);
                        Check(!garden.TryAct(GardenTool.Plant,cell)&&garden.Plots[cell].crop<0,"No seeds blocks planting");
                        garden.Inventory.TryAdd(garden.SeedId(0),4);
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down*3);Check(garden.TryAct(GardenTool.Plant,cell),"Queue with seeds");
                        garden.Inventory.TryRemove(garden.SeedId(0),4);break;
                    case 3:
                        if(garden.IsApproaching)return;
                        Check(garden.Plots[cell].crop<0,"Recheck seeds on arrival");garden.Inventory.TryAdd(garden.SeedId(0),4);Fill();
                        target=garden.environment.GetComponentsInChildren<FarmClearable>().First(t=>t.name=="Grass Tuft");
                        target.transform.position+=(Vector3)((Vector2)garden.CellCenter(cell+Vector3Int.right)-target.Center);Physics2D.SyncTransforms();
                        loot=new Dictionary<string,int>(target.Loot(garden));
                        Check(!garden.TryClear(target)&&target.Available,"Full inventory preserves nature");
                        Check(target.Loot(garden).OrderBy(p=>p.Key).SequenceEqual(loot.OrderBy(p=>p.Key)),"Reward not rerolled");
                        garden.Inventory.TryRemove("stone",99);before=loot.ToDictionary(p=>p.Key,p=>garden.Inventory.Count(p.Key));
                        Check(garden.TryClear(target),"Clear with space");break;
                    case 4:
                        if(garden.IsApproaching)return;
                        Check(target.Available,"No loot before clip ends");
                        foreach(var p in before)Check(garden.Inventory.Count(p.Key)==p.Value,"No early reward");
                        garden.player.GetComponent<FarmPlayerController>().CancelWork();
                        Check(target.Available,"Cancel animation preserves target");
                        Check(garden.TryClear(target),"Restart clear");break;
                    case 5:
                        if(garden.IsBusy)return;
                        Check(!target.Available,"Clear finished");
                        foreach(var p in loot)Check(garden.Inventory.Count(p.Key)==before[p.Key]+p.Value,"Exact reward granted");
                        Check(!garden.TryClear(target),"No duplicate loot");
                        garden.Inventory.TryRemove("stone",garden.Inventory.Count("stone"));garden.Inventory.TryAdd("stone",12);
                        garden.Inventory.TryAdd("twig",3);garden.Inventory.TryAdd("mushroom",2);garden.Inventory.TryAdd("flower",4);
                        garden.InventoryUI.Open();
                        Check(garden.InventoryUI.IsOpen && garden.InventoryUI.ContainsPointer(Vector2.zero),"Open UI blocks world input");
                        var source=UnityEngine.Object.FindObjectsByType<FarmInventorySlotUI>(FindObjectsSortMode.None).Single(s=>s.Index==0);
                        var destination=UnityEngine.Object.FindObjectsByType<FarmInventorySlotUI>(FindObjectsSortMode.None).Single(s=>s.Index==23);
                        var drag=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=source.transform.position,pointerCurrentRaycast=new RaycastResult{gameObject=destination.gameObject}};
                        int amount=garden.Inventory.GetSlot(0).Count;string id=garden.Inventory.GetSlot(0).Item;
                        ExecuteEvents.Execute(source.gameObject,drag,ExecuteEvents.beginDragHandler);ExecuteEvents.Execute(source.gameObject,drag,ExecuteEvents.endDragHandler);
                        Check(garden.Inventory.GetSlot(23).Item==id&&garden.Inventory.GetSlot(23).Count==amount,"UI drag moves stack");
                        garden.Inventory.Move(23,0);
                        Check(garden.Inventory.Item("twig").Icon!=null&&garden.Inventory.Item("mushroom").Icon!=null,"Resource icons assigned");
                        break;
                    case 6:
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Inventory-PlayMode.png");break;
                    case 7:
                        keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();InputSystem.onBeforeUpdate+=FeedKey;
                        keyState=new KeyboardState(Key.I);break;
                    case 8:
                        Check(!garden.InventoryUI.IsOpen,"I closes inventory");keyState=new KeyboardState();break;
                    case 9:
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Inventory-Button.png");
                        keyState=new KeyboardState(Key.I);break;
                    case 10:
                        Check(garden.InventoryUI.IsOpen,"I opens inventory");keyState=new KeyboardState();break;
                    case 11:
                        keyState=new KeyboardState(Key.Escape);break;
                    case 12:
                        Check(!garden.InventoryUI.IsOpen,"Escape closes inventory");keyState=new KeyboardState();
                        UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Inventory button").onClick.Invoke();
                        Check(garden.InventoryUI.IsOpen,"Backpack button opens inventory");
                        UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Close inventory").onClick.Invoke();
                        Check(!garden.InventoryUI.IsOpen,"Cross closes inventory");
                        garden.worldCamera.GetComponent<FarmCameraFollow>().SnapToTarget();garden.GetComponent<FarmGardenUI>().OpenMenu(cell);
                        UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Tool Plant").onClick.Invoke();
                        garden.Inventory.TryRemove(garden.SeedId(1),garden.Inventory.Count(garden.SeedId(1)));
                        break;
                    case 13:
                        var seedButton=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Seed 1");
                        Check(!seedButton.interactable&&seedButton.transform.Find("Seed count").GetComponent<TMP_Text>().text=="0","Empty seed type dimmed with zero count");
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Inventory-Seed-Menu.png");break;
                    case 14:
                        Finish(null);return;
                }
                step++;after=Time.time+.2f;
            }
            catch(Exception e){Finish(e.ToString());}
        }
    }
}
