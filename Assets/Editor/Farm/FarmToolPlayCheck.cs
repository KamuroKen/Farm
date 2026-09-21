using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmToolPlayCheck
    {
        private static FarmGardenSystem garden;
        private static int step = -1, direction, checks;
        private static float after;
        private static Vector3Int cell;
        private static readonly Vector2[] Positions = {Vector2.up, Vector2.right, Vector2.left, Vector2.down};
        static FarmToolPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Farm.ToolCheck",false)) { step=0; direction=0; checks=0; after=Time.time+.5f; }
            };
        }
        private static void Check(bool condition, string description) { if(!condition) throw new Exception(description); checks++; }
        private static void Move(Vector2 position)
        {
            garden.player.position = position;
            garden.player.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Library/FarmToolCheck.request"))
            { File.Delete("Library/FarmToolCheck.request"); SessionState.SetBool("Farm.ToolCheck",true); EditorApplication.EnterPlaymode(); return; }
            if(!EditorApplication.isPlaying || step<0 || Time.time<after) return;
            try
            {
                var worker = garden == null ? null : garden.player.GetComponent<FarmPlayerController>();
                switch(step)
                {
                    case 0:
                        garden = UnityEngine.Object.FindFirstObjectByType<FarmGardenSystem>();
                        Move(new Vector2(-18,-10));
                        bool found=false;
                        for(int y=-9;y<=-7 && !found;y++) for(int x=-21;x<=-17 && !found;x++)
                        { var c=new Vector3Int(x,y,0); if(garden.CanAct(GardenTool.Place,c,out _)) {cell=c;found=true;} }
                        Check(found,"Free cell"); Check(garden.TryAct(GardenTool.Place,cell),"Place");
                        Move((Vector2)garden.CellCenter(cell)+Vector2.up*3);
                        Check(garden.TryAct(GardenTool.Till,cell) && garden.IsApproaching,"Distant action queues approach"); garden.CancelApproach();
                        break;
                    case 1:
                        Move((Vector2)garden.CellCenter(cell)+Positions[direction]);
                        Check(garden.TryAct(GardenTool.Till,cell),"Start scythe");
                        Check(garden.IsBusy && !garden.Plots[cell].tilled,"No early till");
                        Check(!garden.TryAct(GardenTool.Till,cell),"Reject duplicate");
                        Check(garden.player.GetComponent<Animator>().GetInteger("Direction")==direction,"Scythe direction");
                        break;
                    case 2:
                        Check(garden.IsBusy && !garden.Plots[cell].tilled,"Till still pending");
                        Check(garden.player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero,"Movement stopped");
                        after=Time.time+1; step++; return;
                    case 3:
                        Check(!garden.IsBusy && garden.Plots[cell].tilled,"Till completed");
                        garden.SelectCrop(0); Check(garden.TryAct(GardenTool.Plant,cell),"Plant instant");
                        Check(garden.Plots[cell].crop==0 && !garden.IsBusy,"Plant has no animation");
                        Check(garden.TryAct(GardenTool.Water,cell),"Start watering");
                        Check(!garden.Plots[cell].watered && garden.Plots[cell].growth==0,"No early water or growth");
                        Check(garden.player.GetComponent<Animator>().GetInteger("Direction")==direction,"Water direction");
                        garden.worldCamera.GetComponent<FarmCameraFollow>().SnapToTarget();
                        break;
                    case 4:
                        Check(garden.IsBusy && !garden.Plots[cell].watered,"Water still pending");
                        ScreenCapture.CaptureScreenshot("BuildArtifacts/Watering-"+direction+".png");
                        after=Time.time+1; step++; return;
                    case 5:
                        Check(!garden.IsBusy && garden.Plots[cell].watered,"Water completed");
                        Check(garden.player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Idle"+new[]{"Down","Left","Right","Up"}[direction]),"Return to directional idle");
                        garden.AdvanceGrowth(15); Check(garden.TryAct(GardenTool.Harvest,cell),"Harvest");
                        if(++direction<4) { garden.Plots[cell].tilled=false; step=1; after=Time.time+.1f; return; }
                        garden.Plots[cell].tilled=false;
                        Check(garden.TryAct(GardenTool.Till,cell),"Begin cancellation check");
                        worker.CancelWork();
                        Check(!garden.IsBusy && !garden.Plots[cell].tilled,"Cancelled animation does not apply action");
                        File.WriteAllText("BuildArtifacts/farm-tool-check.txt","PASS: "+checks+" checks, four directions, delayed effects, range, cancellation and instant planting.");
                        step=-1; SessionState.SetBool("Farm.ToolCheck",false); EditorApplication.ExitPlaymode(); return;
                }
                step++; after=Time.time+.2f;
            }
            catch(Exception e) { File.WriteAllText("BuildArtifacts/farm-tool-check.txt",e.ToString()); step=-1; SessionState.SetBool("Farm.ToolCheck",false); EditorApplication.ExitPlaymode(); }
        }
    }
}

