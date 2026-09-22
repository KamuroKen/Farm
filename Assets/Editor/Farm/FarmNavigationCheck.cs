using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmNavigationCheck
    {
        private static int step=-1, checks;
        private static float after, deadline;
        private static FarmGardenSystem garden;
        private static Vector3Int cell;
        private static Vector2 start;
        private static GameObject wall;
        private static Keyboard keyboard;
        static FarmNavigationCheck()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=> {
                if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Farm.NavCheck",false))
                {step=0;checks=0;after=Time.time+.6f;deadline=Time.time+30;}
            };
        }
        private static void Check(bool value,string message) {if(!value) throw new Exception(message);checks++;}
        private static void Move(Vector2 position) {garden.player.position=position;garden.player.GetComponent<Rigidbody2D>().position=position;Physics2D.SyncTransforms();}
        private static void Finish(string error)
        {
            if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            File.WriteAllText("BuildArtifacts/farm-navigation-check.txt",error ?? "PASS: "+checks+" navigation checks");
            step=-1; SessionState.SetBool("Farm.NavCheck",false); EditorApplication.ExitPlaymode();
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Library/FarmNavigationCheck.request"))
            {File.Delete("Library/FarmNavigationCheck.request");SessionState.SetBool("Farm.NavCheck",true);EditorApplication.EnterPlaymode();return;}
            if(!EditorApplication.isPlaying || step<0 || Time.time<after) return;
            try
            {
                if(Time.time>deadline) throw new Exception("Navigation timed out at step "+step+" position "+garden.player.position+" feedback "+garden.Feedback);
                switch(step)
                {
                    case 0:
                        garden=UnityEngine.Object.FindFirstObjectByType<FarmGardenSystem>();
                        cell=new Vector3Int(-19,-8,0);start=(Vector2)garden.CellCenter(cell)+Vector2.down*3.5f;
                        Move(start); Check(garden.TryAct(GardenTool.Place,cell),"Build target");
                        wall=new GameObject("Navigation test wall",typeof(BoxCollider2D));
                        wall.transform.position=garden.CellCenter(cell)+Vector3.down*1.9f;
                        wall.GetComponent<BoxCollider2D>().size=new Vector2(2,.3f);Physics2D.SyncTransforms();
                        var finder=new FarmPlotPathfinder(garden.ground,garden.player);
                        Check(finder.Find(start,garden.CellCenter(cell),out var route),"Find route around wall");
                        Check(route.Any(p=>Mathf.Abs(p.x-start.x)>1),"Route detours around wall");
                        Check(finder.CanWorkFrom(route.Last(), garden.CellCenter(cell)),"Route ends at exact working side");
                        Check(!finder.CanWorkFrom((Vector2)garden.CellCenter(cell)+new Vector2(.8f,.8f),garden.CellCenter(cell)),"Diagonal working position rejected");
                        Check(garden.CanAct(GardenTool.Till,cell,out _),"Distant action enabled");
                        Check(garden.TryAct(GardenTool.Till,cell) && garden.IsApproaching,"Command starts walking");
                        Check(!garden.Plots[cell].tilled,"No premature action");
                        break;
                    case 1:
                        if(garden.IsApproaching || garden.IsBusy) return;
                        Check(garden.Plots[cell].tilled,"Walk then animate then till");
                        Check(new FarmPlotPathfinder(garden.ground,garden.player).CanWorkFrom(garden.player.position,garden.CellCenter(cell)),"Stops aligned with working side");
                        Move(start); garden.SelectCrop(2); Check(garden.TryAct(GardenTool.Plant,cell),"Queue planting");
                        garden.SelectCrop(4);
                        break;
                    case 2:
                        if(garden.IsApproaching) return;
                        Check(garden.Plots[cell].crop==2,"Queued seed choice preserved");
                        Move(start);Check(garden.TryAct(GardenTool.Water,cell),"Queue watering");
                        garden.CancelApproach();Check(!garden.IsApproaching && !garden.Plots[cell].watered,"Cancel prevents action");
                        Check(garden.TryAct(GardenTool.Water,cell),"Reissue water");
                        Check(garden.TryAct(GardenTool.Water,cell),"Replace approach command");
                        break;
                    case 3:
                        if(garden.IsApproaching || garden.IsBusy) return;
                        Check(garden.Plots[cell].watered,"Replaced command completes");
                        garden.AdvanceGrowth(15);Move(start);Check(garden.TryAct(GardenTool.Harvest,cell),"Queue harvest");
                        keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                        break;
                    case 4:
                        Check(!garden.IsApproaching && garden.Plots[cell].crop==2,"WASD cancels queued harvest");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                        break;
                    case 5:
                        Move(start);Check(garden.TryAct(GardenTool.Harvest,cell),"Queue harvest again");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
                        break;
                    case 6:
                        Check(!garden.IsApproaching && garden.Plots[cell].crop==2,"Escape cancels approach");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                        break;
                    case 7:
                        Move(start);Check(garden.TryAct(GardenTool.Harvest,cell),"Start dynamic obstruction test");
                        wall.transform.position=garden.player.position;wall.GetComponent<BoxCollider2D>().size=new Vector2(2,2);Physics2D.SyncTransforms();
                        break;
                    case 8:
                        Check(!garden.IsApproaching && garden.Plots[cell].crop==2,"New obstruction stops movement without action");
                        wall.transform.position=garden.CellCenter(cell);wall.GetComponent<BoxCollider2D>().size=new Vector2(4,4);Physics2D.SyncTransforms();Move(start);
                        Check(!garden.TryAct(GardenTool.Harvest,cell),"Unreachable command rejected");
                        Check(garden.Feedback=="Can't reach this plot","Reachability feedback");
                        Finish(null);return;
                }
                step++;after=Time.time+.3f;
            }
            catch(Exception e){Finish(e.ToString());}
        }
    }
}
