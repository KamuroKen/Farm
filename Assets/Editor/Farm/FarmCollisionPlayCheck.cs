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
    public static class FarmCollisionPlayCheck
    {
        const string Flag="Farm.CollisionCheck",Request="Library/FarmCollisionPlay.request",Pass="BuildArtifacts/collision-play-pass.txt";
        static FarmPlayerController player;static Rigidbody2D body;static Collider2D shape;static Keyboard keyboard;
        static int stage=-1;static float after;static Vector2 offset,start,direction;static Collider2D target;static SpriteRenderer tree;static int checks;
        static FarmCollisionPlayCheck()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(!SessionState.GetBool(Flag,false))return;
                if(state==PlayModeStateChange.EnteredPlayMode)
                {
                    player=UnityEngine.Object.FindAnyObjectByType<FarmPlayerController>();body=player.GetComponent<Rigidbody2D>();shape=player.GetComponent<Collider2D>();
                    offset=(Vector2)shape.bounds.center-(Vector2)player.transform.position;
                    keyboard=InputSystem.AddDevice<Keyboard>("CollisionCheckKeyboard");keyboard.MakeCurrent();checks=0;stage=0;after=Time.time+.5f;
                }
                else if(state==PlayModeStateChange.ExitingPlayMode){if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);stage=-1;}
            };
        }
        static void Move(Vector2 foot)
        {body.linearVelocity=Vector2.zero;body.position=foot-offset;player.transform.position=body.position;Physics2D.SyncTransforms();start=foot;}
        static void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        static Vector2 Foot=>(Vector2)shape.bounds.center;
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;File.AppendAllText("BuildArtifacts/collision-play-log.txt","PASS: "+label+"\n");}
        static void Against(string prefix)
        {
            Keys();
            foreach(var r in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Where(r=>r.sprite!=null&&r.sprite.name.StartsWith(prefix)))
            {
                var c=r.GetComponent<Collider2D>();if(c==null||c.isTrigger)continue;
                foreach(var side in new[]{Vector2.right,Vector2.left,Vector2.down,Vector2.up})
                {
                    float extent=Mathf.Abs(side.x)*(c.bounds.extents.x+shape.bounds.extents.x)+Mathf.Abs(side.y)*(c.bounds.extents.y+shape.bounds.extents.y);
                    var point=(Vector2)c.bounds.center+side*(extent+.65f);
                    if(Physics2D.OverlapBoxAll(point,shape.bounds.size,0).Any(o=>o!=shape&&!o.isTrigger))continue;
                    if(Physics2D.BoxCastAll(point,shape.bounds.size,0,-side,.65f).Any(h=>h.collider!=shape&&h.collider!=c&&!h.collider.isTrigger))continue;
                    target=c;direction=side;Move(point);Keys(side.x>0?Key.A:side.x<0?Key.D:side.y>0?Key.S:Key.W);return;
                }
            }
            throw new Exception("No clear real collision sample for "+prefix);
        }
        static void HitCheck(string label)
        {
            float separation=Vector2.Dot(Foot-(Vector2)target.bounds.center,direction);
            float limit=Mathf.Abs(direction.x)*(target.bounds.extents.x+shape.bounds.extents.x)+Mathf.Abs(direction.y)*(target.bounds.extents.y+shape.bounds.extents.y);
            var distance=Physics2D.Distance(shape,target);
            File.AppendAllText("BuildArtifacts/collision-play-log.txt",$"DEBUG {label}: start={start} foot={Foot} target={target.name} targetBounds={target.bounds} shapeBounds={shape.bounds} direction={direction} separation={separation:F3} limit={limit:F3} physicsDistance={distance.distance:F3} overlap={distance.isOverlapped} playerLayer={body.gameObject.layer} targetLayer={target.gameObject.layer} ignored={Physics2D.GetIgnoreLayerCollision(body.gameObject.layer,target.gameObject.layer)}\n");
            Check(Vector2.Distance(Foot,start)>.2f,label+" movement was exercised");
            Check(separation>=limit-.045f,label+" stops the moving player");Keys();
        }
        static void Finish(string error)
        {
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            keyboard=null;stage=-1;SessionState.SetBool(Flag,false);
            if(error==null)File.WriteAllText(Pass,"PASS: "+checks+" real Play Mode movement/sorting checks.\n");
            else File.AppendAllText("BuildArtifacts/collision-play-log.txt","FAIL: "+error+"\n");
            EditorApplication.ExitPlaymode();
        }
        static void Tick()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists(Request))
            {
                File.Delete(Request);if(File.Exists(Pass))File.Delete(Pass);File.WriteAllText("BuildArtifacts/collision-play-log.txt","");SessionState.SetBool(Flag,true);EditorApplication.EnterPlaymode();return;
            }
            if(stage<0||!EditorApplication.isPlaying||Time.time<after)return;
            try
            {
                switch(stage)
                {
                    case 0:Against("barrel_");break;
                    case 1:HitCheck("Barrel");Against("rock_");break;
                    case 2:HitCheck("Rock");Against("tree_round");break;
                    case 3:HitCheck("Tree trunk");Move(new Vector2(16.5f,-6.5f));Keys(Key.S);break;
                    case 4:File.AppendAllText("BuildArtifacts/collision-play-log.txt",$"DEBUG Lake: start={start} foot={Foot} playerBounds={shape.bounds}\n");Check(Foot.y>-9+shape.bounds.extents.y-.05f,"Lake shore blocks WASD movement");Check(Foot.y<start.y-.2f,"Lake approach actually moved");Move(new Vector2(3,17));Keys(Key.W);break;
                    case 5:File.AppendAllText("BuildArtifacts/collision-play-log.txt",$"DEBUG River: start={start} foot={Foot} playerBounds={shape.bounds}\n");Check(Foot.y<19-shape.bounds.extents.y+.05f,"River bank blocks WASD movement");Check(Foot.y>start.y+.2f,"River approach actually moved");Move(new Vector2(-1,17));Keys(Key.W);break;
                    case 6:Check(Foot.y>20,"River pier stays walkable");Move(new Vector2(1,22));Keys(Key.D);break;
                    case 7:Check(Foot.x<1.7f,"River pier edge blocks walking into water");Move(new Vector2(9,-10.25f));Keys(Key.S);break;
                    case 8:File.AppendAllText("BuildArtifacts/collision-play-log.txt",$"DEBUG Pond entry: start={start} foot={Foot} shape={shape.bounds}\n");Check(Foot.y<-11.4f,"Pond pier entrance remains open");Move(new Vector2(9,-13));Keys(Key.D);break;
                    case 9:Check(Foot.x<10.7f,"Pond pier edge blocks walking into water");Keys();
                        tree=UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).First(r=>r.sprite!=null&&r.sprite.name=="tree_round");
                        var trunk=tree.GetComponent<Collider2D>();var trunkY=trunk!=null?trunk.bounds.center.y:tree.bounds.min.y+.2f;
                        Move(new Vector2(tree.bounds.center.x+tree.bounds.extents.x+.2f,trunkY+.8f));break;
                    case 10:Check(player.GetComponent<SpriteRenderer>().sortingOrder<tree.sortingOrder,"Player draws behind tree when north of trunk");
                        var frontTrunk=tree.GetComponent<Collider2D>();var frontY=frontTrunk!=null?frontTrunk.bounds.center.y:tree.bounds.min.y+.2f;
                        Move(new Vector2(tree.bounds.center.x+tree.bounds.extents.x+.2f,frontY-.8f));break;
                    case 11:Check(player.GetComponent<SpriteRenderer>().sortingOrder>tree.sortingOrder,"Player draws before tree when south of trunk");Move(new Vector2(-19,8));break;
                    case 12:Check(player.GetComponent<SpriteRenderer>().sortingOrder==100,"Player restores base order away from trees");Finish(null);return;
                }
                stage++;after=Time.time+(stage==6?1.15f:1f);
            }
            catch(Exception e){Finish(e.ToString());}
        }
    }
}
