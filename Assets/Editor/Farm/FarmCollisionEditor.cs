using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmCollisionEditor
    {
        const string Request="Library/FarmCollision.request";
        public const string Report="BuildArtifacts/collision-validation.txt";
        static readonly string[] Props={"barrel_","bench_","trough","hay_bale","crate_","rock_","rocks_","stump","fallen_log","notice_board","sign_","mailbox_","bucket","bush_","flower_box_","well_"};
        static FarmCollisionEditor(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
            string command=File.ReadAllText(Request).Trim();File.Delete(Request);
            try { if(command=="trees") DenseTrees();else Apply(); }
            catch(Exception e){File.AppendAllText(Report,"FAIL: "+e+"\n");Debug.LogException(e);}
        }
        static Scene Scene()
        {var s=SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");if(!s.IsValid()||!s.isLoaded)s=EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity",OpenSceneMode.Additive);SceneManager.SetActiveScene(s);return s;}
        static Transform Root(Scene s)=>s.GetRootGameObjects().First(g=>g.name=="Farm Environment").transform;
        static int Layer(string name)
        {
            int existing=LayerMask.NameToLayer(name);if(existing>=0)return existing;
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var list=settings.FindProperty("layers");
            for(int i=8;i<32;i++)if(string.IsNullOrEmpty(list.GetArrayElementAtIndex(i).stringValue)){list.GetArrayElementAtIndex(i).stringValue=name;settings.ApplyModifiedProperties();return i;}
            throw new InvalidOperationException("No free collision layer.");
        }
        static bool Solid(SpriteRenderer r)=>r.sprite!=null && Props.Any(p=>r.sprite.name.StartsWith(p,StringComparison.Ordinal));
        static Transform New(string name,Transform parent,int layer)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.gameObject.layer=layer;return t;}
        [MenuItem("Tools/Farm/Fix Object and Water Collisions")]
        public static void Apply()
        {
            var scene=Scene();var root=Root(scene);
            var backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeCollisionFix.unity");
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Backup failed.");
            Directory.CreateDirectory("BuildArtifacts");File.WriteAllText(Report,"Backup: "+backup+"\n");
            int obstacles=Layer("Farm Obstacles"),water=Layer("Farm Water"),trees=Layer("Farm Trees"),playerLayer=Layer("Farm Player");
            foreach(int l in new[]{obstacles,water,trees})Physics2D.IgnoreLayerCollision(playerLayer,l,false);
            var player=UnityEngine.Object.FindAnyObjectByType<FarmPlayerController>();player.gameObject.layer=playerLayer;
            var body=player.GetComponent<Rigidbody2D>();body.bodyType=RigidbodyType2D.Dynamic;body.simulated=true;body.gravityScale=0;body.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
            foreach(var c in player.GetComponents<Collider2D>()){c.enabled=true;c.isTrigger=false;c.excludeLayers=0;}
            if(player.GetComponent<FarmPlayerTreeSorting>()==null)player.gameObject.AddComponent<FarmPlayerTreeSorting>();
            int count=0;
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>())
            {
                if(!Solid(r))continue;
                r.gameObject.layer=obstacles;
                var c=r.GetComponent<BoxCollider2D>();if(c==null)c=r.gameObject.AddComponent<BoxCollider2D>();
                var b=r.bounds;float width=Mathf.Max(.18f,b.size.x*.78f),height=Mathf.Clamp(b.size.y*.32f,.18f,.65f);
                c.size=new Vector2(width/r.transform.lossyScale.x,height/r.transform.lossyScale.y);
                c.offset=r.transform.InverseTransformPoint(new Vector3(b.center.x,b.min.y+height*.5f+.06f,0));c.isTrigger=false;c.enabled=true;count++;
            }
            foreach(var c in root.GetComponentsInChildren<Collider2D>())
            {
                var renderer=c.GetComponent<SpriteRenderer>();
                bool tree=c.name=="Tree trunk" || (renderer!=null && renderer.sprite!=null && renderer.sprite.name.StartsWith("tree_"));
                c.gameObject.layer=tree?trees:obstacles;
            }
            var docks=new List<FarmWalkableSurface>();
            var pondJetty=root.Find("Pond/Jetty");
            if(pondJetty!=null)
            {
                var deck=pondJetty.GetComponentsInChildren<SpriteRenderer>().First(r=>r.sprite!=null&&r.sprite.name=="dock_vertical_panel");
                foreach(int y in new[]{-11,-10})
                {
                    var name="Landward dock panel "+y;var panel=pondJetty.Find(name);
                    if(panel==null)panel=New(name,pondJetty,pondJetty.gameObject.layer);
                    panel.position=deck.transform.position+Vector3.up*(y-deck.bounds.center.y);
                    var renderer=panel.GetComponent<SpriteRenderer>();if(renderer==null)renderer=panel.gameObject.AddComponent<SpriteRenderer>();
                    renderer.sprite=deck.sprite;renderer.sharedMaterial=deck.sharedMaterial;
                    renderer.sortingLayerID=deck.sortingLayerID;renderer.sortingOrder=deck.sortingOrder;
                }
            }
            foreach(var path in new[]{"Pond/Jetty","Settlement Revision/River pier"})
            {
                var t=root.Find(path);if(t==null)continue;
                var d=t.GetComponent<FarmWalkableSurface>();if(d==null)d=t.gameObject.AddComponent<FarmWalkableSurface>();d.Refresh();docks.Add(d);
            }
            foreach(var c in root.GetComponentsInChildren<Collider2D>().Where(c=>c.name=="Water"||c.name=="River water").ToArray())UnityEngine.Object.DestroyImmediate(c.gameObject);
            var old=root.Find("Collisions/Water from terrain");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var parent=New("Water from terrain",root.Find("Collisions"),water);
            var ground=root.Find("Terrain/01 Grass and water/Tiles").GetComponent<Tilemap>();
            var cells=new HashSet<Vector2Int>();
            foreach(var p in ground.cellBounds.allPositionsWithin)
            {
                if(ground.GetSprite(p)?.name!="ground_water")continue;
                var min=ground.CellToWorld(p);
                for(int y=0;y<2;y++)for(int x=0;x<2;x++)
                {
                    var q=new Vector2Int(Mathf.RoundToInt(min.x*2)+x,Mathf.RoundToInt(min.y*2)+y);var center=(Vector2)q*.5f+Vector2.one*.25f;
                    if(!docks.Any(d=>d.Contains(center)))cells.Add(q);
                }
            }
            int boxes=0;
            foreach(var row in cells.GroupBy(p=>p.y))
            {
                var xs=row.Select(p=>p.x).OrderBy(x=>x).ToArray();
                for(int i=0;i<xs.Length;)
                {
                    int first=xs[i],last=first;while(++i<xs.Length&&xs[i]==last+1)last=xs[i];
                    var t=New("Water barrier",parent,water);t.position=new Vector3((first+last+1)*.25f,row.Key*.5f+.25f,0);
                    t.gameObject.AddComponent<BoxCollider2D>().size=new Vector2((last-first+1)*.5f,.5f);boxes++;
                }
            }
            Physics2D.SyncTransforms();
            foreach(var q in cells)if(!Physics2D.OverlapPoint((Vector2)q*.5f+Vector2.one*.25f,1<<water))throw new InvalidOperationException("Water gap at "+q);
            if(root.GetComponentsInChildren<SpriteRenderer>().Where(Solid).Any(r=>r.GetComponent<Collider2D>()==null))throw new InvalidOperationException("Prop collision missing.");
            foreach(var p in new[]{new Vector2(-1,19),new Vector2(-1,22),new Vector2(9,-14)})if(Physics2D.OverlapPoint(p,1<<water))throw new InvalidOperationException("Dock blocked by water.");
            File.AppendAllText(Report,$"PASS: {count} solid props; {cells.Count} water half-cells covered by {boxes} barriers; both docks clear; four physics layers assigned.\n");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        // Intentionally invoked only AFTER the collision and sorting Play Mode check passes.
        public static void DenseTrees()
        {
            if(!File.Exists("BuildArtifacts/collision-play-pass.txt"))throw new InvalidOperationException("Run the gameplay checks before adding trees.");
            var scene=Scene();var root=Root(scene);
            var group=root.Find("Dense roadside woodland");
            if(group==null)
            {
                group=New("Dense roadside woodland",root,LayerMask.NameToLayer("Farm Trees"));
                var ground=root.Find("Terrain/01 Grass and water/Tiles").GetComponent<Tilemap>();var roads=root.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
                var seeds=root.GetComponentsInChildren<SpriteRenderer>().Where(r=>r.sprite!=null&&r.sprite.name.StartsWith("tree_")&&r.bounds.center.x>3&&r.bounds.center.x<28).ToArray();
                int count=0;
                foreach(var seed in seeds.OrderBy(r=>r.bounds.min.y))
                {
                    var basePoint=new Vector2(seed.bounds.center.x,seed.bounds.min.y);
                    if(!Enumerable.Range(-6,13).Any(i=>roads.HasTile(roads.WorldToCell(basePoint+Vector2.right*i*.5f))) && !Enumerable.Range(-6,13).Any(i=>roads.HasTile(roads.WorldToCell(basePoint+Vector2.up*i*.5f))))continue;
                    foreach(var offset in new[]{new Vector2(.95f,.55f),new Vector2(-.85f,1.2f)})
                    {
                        var p=basePoint+offset;if(p.x>28.5f||p.y>14.5f||p.y<-19)continue;
                        bool clear=true;
                        foreach(var delta in new[]{Vector2.zero,new Vector2(-.7f,0),new Vector2(.7f,0),new Vector2(0,.6f)})
                            if(ground.GetSprite(ground.WorldToCell(p+delta))?.name!="ground_grass"||roads.HasTile(roads.WorldToCell(p+delta)))clear=false;
                        if(!clear || Physics2D.OverlapCircleAll(p+Vector2.up*.34f,.6f).Length>0)continue;
                        var t=New("Cluster tree",group,group.gameObject.layer);t.position=p+Vector2.left;
                        var r=t.gameObject.AddComponent<SpriteRenderer>();r.sprite=seed.sprite;r.sharedMaterial=seed.sharedMaterial;r.spriteSortPoint=SpriteSortPoint.Pivot;
                        // Use narrow two-cell sprites for controlled overlapping canopies.
                        if(r.sprite.bounds.size.x>2.1f){UnityEngine.Object.DestroyImmediate(t.gameObject);continue;}
                        var c=t.gameObject.AddComponent<BoxCollider2D>();c.size=new Vector2(.5f,.4375f);c.offset=new Vector2(1,.34375f);
                        FarmSortingEditor.SetSourceDepth(t.gameObject,Mathf.RoundToInt((24-p.y)*64));Physics2D.SyncTransforms();count++;
                        if(count>=32)break;
                    }
                    if(count>=32)break;
                }
                File.AppendAllText(Report,"Final landscaping: "+count+" overlapping roadside trees, each with trunk collision.\n");
            }
            FarmSortingEditor.Normalize(scene);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            FarmRoadEditor.Capture();File.Copy("BuildArtifacts/FarmLevel-Roads.png","BuildArtifacts/FarmLevel-CollisionFix.png",true);EditorSceneManager.SaveScene(scene);
            File.AppendAllText(Report,"PASS: sorting normalized and final scene preview updated.\n");
        }
    }
}
