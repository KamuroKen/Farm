using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmClearingCheck
    {
        private static int step=-1,index,checks;
        private static float after,deadline;
        private static FarmGardenSystem garden;
        private static FarmClearable target;
        private static Vector3Int cell=new Vector3Int(-19,-8,0);
        private static readonly string[] Names={"Grass Tuft","Bush Dense","Rock Blue Small","Clover Patch","Flowers White"};
        static FarmClearingCheck()
        {
            EditorApplication.update+=Tick;
            EditorApplication.delayCall += () => {
                if(EditorApplication.isPlaying && SessionState.GetBool("Farm.ClearCheck",false) && step<0) Begin();
            };
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Farm.ClearCheck",false)){Begin();}};
        }
        private static void Begin() {step=0;index=0;checks=0;after=Time.time+.5f;deadline=Time.time+60;File.WriteAllText("BuildArtifacts/farm-clearing-progress.txt","Started");}
        private static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
        private static void Move(Vector2 p){garden.player.position=p;garden.player.GetComponent<Rigidbody2D>().position=p;Physics2D.SyncTransforms();}
        private static void Finish(string error)
        {
            File.WriteAllText("BuildArtifacts/farm-clearing-check.txt",error??"PASS: "+checks+" clearing checks across grass, bush, stone, clover and flowers");
            step=-1;SessionState.SetBool("Farm.ClearCheck",false);EditorApplication.ExitPlaymode();
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Library/FarmClearingCheck.request"))
            {File.Delete("Library/FarmClearingCheck.request");SessionState.SetBool("Farm.ClearCheck",true);EditorApplication.EnterPlaymode();return;}
            if(!EditorApplication.isPlaying || step<0 || Time.time<after)return;
            try
            {
                if(Time.time>deadline)throw new Exception("Timeout step "+step+" "+garden.Feedback);
                File.WriteAllText("BuildArtifacts/farm-clearing-progress.txt","Step "+step+" object "+index+" time "+Time.time);
                switch(step)
                {
                    case 0:
                        garden=UnityEngine.Object.FindFirstObjectByType<FarmGardenSystem>();
                        Check(!garden.environment.GetComponentsInChildren<FarmClearable>().Any(c=>c.name.Contains("Large")||c.name.Contains("Tree")||c.name.Contains("Stump")),"Protected scenery unmarked");
                        break;
                    case 1:
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down*3.5f);
                        Check(garden.CanPlaceAt(cell,out _),"Initially clear ground");
                        target=garden.environment.GetComponentsInChildren<FarmClearable>().First(c=>c.name==Names[index]);
                        target.transform.position+=(Vector3)((Vector2)garden.CellCenter(cell)-target.Center);Physics2D.SyncTransforms();
                        Check(!garden.CanPlaceAt(cell,out _),"Scenery blocks plot placement");
                        Check(FarmClearable.Pick(garden.environment,target.Center)==target,"Object picked");
                        garden.worldCamera.GetComponent<FarmCameraFollow>().SnapToTarget();
                        garden.GetComponent<FarmGardenUI>().OpenClearMenu(target);
                        Check(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b=>b.name=="Clear"&&b.interactable),"Clear button visible and enabled");
                        Check(garden.TryClear(target),"Clear command accepted");
                        Check(target.Available,"Object remains during approach");
                        garden.CancelApproach();Check(target.Available&&!garden.IsApproaching,"Cancel approach preserves object");
                        Check(garden.TryClear(target),"Reissue clearing");
                        break;
                    case 2:
                        if(garden.IsApproaching)return;
                        Check(garden.IsBusy && target.Available,"Scythe starts before removal");
                        Check(!garden.TryClear(target),"Duplicate clear blocked");
                        garden.player.GetComponent<FarmPlayerController>().CancelWork();
                        Check(target.Available && !garden.IsBusy,"Cancelled clip preserves object");
                        Check(garden.TryClear(target),"Clear restarts");
                        garden.worldCamera.GetComponent<FarmCameraFollow>().SnapToTarget();
                        break;
                    case 3:
                        Check(target.Available&&garden.IsBusy,"Object remains mid animation");
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Clearing-"+index+".png");
                        after=Time.time+1;step++;return;
                    case 4:
                        Check(!target.gameObject.activeInHierarchy&&!garden.IsBusy,"Whole object disabled after clip");
                        Check(target.GetComponentsInChildren<Collider2D>(true).All(c=>!c.gameObject.activeInHierarchy),"Colliders disabled with object");
                        Check(!garden.TryClear(target),"Cleared object cannot be cleared twice");
                        Move((Vector2)garden.CellCenter(cell)+Vector2.down*3.5f);
                        Check(garden.CanPlaceAt(cell,out _),"Ground available after clearing");
                        if(++index<Names.Length){step=1;after=Time.time+.1f;return;}
                        Check(garden.TryAct(GardenTool.Place,cell),"Build plot on cleared ground");
                        Check(FarmClearable.Pick(garden.environment,garden.CellCenter(cell))==null,"Player plot is not clearable");
                        Finish(null);return;
                }
                step++;after=Time.time+.15f;
            }
            catch(Exception e){Finish(e.ToString());}
        }
    }
}
