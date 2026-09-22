using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmSettlementEditor
    {
        [Serializable] class Plan { public Building[] buildings; public SpriteSpec[] sprites; }
        [Serializable] class SpriteSpec { public string name,atlas; public int x,y,w,h; public float pivotX,pivotY; }
        [Serializable] class Building { public string name; public float bottom; public Part[] parts; public Footprint[] colliders; }
        [Serializable] class Part { public string name,sprite; public float x,y; public int order; }
        [Serializable] class Footprint { public float x,y,w,h; }
        [Serializable] class Roads { public Cell[] tiles; }
        [Serializable] class Cell { public string sprite; public int x,y; }
        const string Request="Library/FarmSettlement.request", Report="BuildArtifacts/settlement-validation.txt";
        static Transform root, revision;
        static Tilemap ground, roads, soil;
        static Dictionary<string,Sprite> sprites;
        static Material material;
        static readonly Bounds Field=new Bounds(new Vector3(-18.5f,9,0),new Vector3(15,12,2));
        static FarmSettlementEditor() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request))return;
            File.Delete(Request);
            try { Apply(); } catch(Exception e){File.AppendAllText(Report,"FAIL: "+e+"\n");Debug.LogException(e);}
        }
        [MenuItem("Tools/Farm/Apply Houses Pasture and River Pier")]
        public static void Apply()
        {
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity",OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            root=scene.GetRootGameObjects().First(g=>g.name=="Farm Environment").transform;
            roads=root.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
            ground=root.Find("Terrain/01 Grass and water/Tiles").GetComponent<Tilemap>();
            soil=root.Find("Terrain/06 Planting ground/Tiles").GetComponent<Tilemap>();
            material=roads.GetComponent<TilemapRenderer>().sharedMaterial;
            ImportBuildingSprites();
            sprites=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Farm/Atlases"}).SelectMany(g=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<Sprite>()).GroupBy(s=>s.name).ToDictionary(g=>g.Key,g=>g.First());
            revision=root.Find("Settlement Revision");
            if(revision==null)
            {
                string backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeSettlement.unity");
                if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Cannot back up scene.");
                File.WriteAllText(Report,"Backup: "+backup+"\n");
                revision=New("Settlement Revision",root);
                ClearOldLayout();
            }
            if(revision.Find("Composition complete")==null)
            {
                Delete("Buildings/Yellow Cottage");Delete("Buildings/Red Barn");
                Undo.DestroyObjectImmediate(revision.gameObject);revision=New("Settlement Revision",root);
                var route=JsonUtility.FromJson<Roads>(File.ReadAllText("Assets/Farm/Design/road-layout.json"));
                roads.ClearAllTiles();roads.SetTiles(route.tiles.Select(t=>new Vector3Int(t.x,t.y,0)).ToArray(),route.tiles.Select(t=>AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Farm/Tiles/"+t.sprite+".asset")).ToArray());roads.CompressBounds();
                BuildFields();BuildBuildings();BuildPasture();BuildPier();BuildTrees();AddFlowerSeeds();
                New("Composition complete",revision);
            }
            RepairPier();
            foreach(var building in new[]{root.Find("Buildings/Yellow Cottage"),root.Find("Buildings/Red Barn")})
                foreach(var renderer in building.GetComponentsInChildren<SpriteRenderer>())
                    if(renderer.sortingOrder<1000) renderer.sortingOrder+=1000;
            foreach(var map in root.GetComponentsInChildren<Tilemap>())map.RefreshAllTiles();
            FarmSortingEditor.Normalize(scene);
            Validate();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Cannot save scene.");
            AssetDatabase.SaveAssets();
            File.AppendAllText(Report,"Saved scene successfully.\n");
            FarmRoadEditor.Capture();
            File.Copy("BuildArtifacts/FarmLevel-Roads.png","BuildArtifacts/FarmLevel-Settlement.png",true);
            EditorSceneManager.SaveScene(scene);
        }
        static void ImportBuildingSprites()
        {
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText("Assets/Farm/Design/settlement-layout.json"));
            foreach(var group in plan.sprites.GroupBy(s=>s.atlas))
            {
                string path="Assets/Farm/Atlases/"+Path.GetFileName(group.Key);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                var factory=new SpriteDataProviderFactories();factory.Init();var provider=factory.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
                var rects=provider.GetSpriteRects().ToList();int height=AssetDatabase.LoadAssetAtPath<Texture2D>(path).height;bool changed=false;
                foreach(var spec in group)
                {
                    if(rects.Any(r=>r.name==spec.name))continue;
                    rects.Add(new SpriteRect{name=spec.name,rect=new Rect(spec.x,height-spec.y-spec.h,spec.w,spec.h),alignment=SpriteAlignment.Custom,pivot=new Vector2(spec.pivotX,spec.pivotY),spriteID=GUID.Generate()});changed=true;
                }
                if(!changed)continue;
                provider.SetSpriteRects(rects.ToArray());provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));provider.Apply();importer.SaveAndReimport();
            }
        }
        static Transform New(string name,Transform parent)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(t.gameObject,"Revise farm settlement");return t;}
        static void Delete(string path) {var t=root.Find(path);if(t!=null)Undo.DestroyObjectImmediate(t.gameObject);}
        static SpriteRenderer Place(string name,string sprite,Vector2 position,Transform parent)
        {
            var t=New(name,parent);t.position=position;var r=t.gameObject.AddComponent<SpriteRenderer>();r.sprite=sprites[sprite];r.sharedMaterial=material;r.spriteSortPoint=SpriteSortPoint.Pivot;
            FarmSortingEditor.SetSourceDepth(t.gameObject,Mathf.RoundToInt((24-position.y)*64));return r;
        }
        static BoxCollider2D Box(string name,Vector2 center,Vector2 size,Transform parent)
        {var t=New(name,parent);t.position=center;var c=t.gameObject.AddComponent<BoxCollider2D>();c.size=size;return c;}
        static void ClearOldLayout()
        {
            Delete("Reference Landscape Revision/Planting field fence");Delete("Reference Landscape Revision/Planting field entrance");
            Delete("Garden");Delete("Barnyard");Delete("Buildings/Cottage");Delete("Buildings/Barn");Delete("Collisions/Buildings/Barn footprint");
            var regions=new[]{Field,new Bounds(new Vector3(-1,9,0),new Vector3(10,8,2)),new Bounds(new Vector3(-16,-6,0),new Vector3(8,9,2)),new Bounds(new Vector3(-15.5f,-15,0),new Vector3(17,10,2))};
            var oldTrees=root.Find("Collisions/Trees").GetComponentsInChildren<BoxCollider2D>().ToList();
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>(true).ToArray())
            {
                if(r==null || r.sprite==null || r.GetComponentInParent<FarmPlayerController>()!=null || !regions.Any(b=>b.Intersects(r.bounds)))continue;
                if(r.sprite.name.StartsWith("tree_") && r.GetComponent<BoxCollider2D>()==null)
                {
                    var foot=new Vector2(r.bounds.center.x,r.bounds.min.y+.34375f);
                    var c=oldTrees.Where(c=>c!=null).OrderBy(c=>Vector2.Distance(c.bounds.center,foot)).FirstOrDefault();
                    if(c!=null && Vector2.Distance(c.bounds.center,foot)<.65f){oldTrees.Remove(c);Undo.DestroyObjectImmediate(c.gameObject);}
                }
                Undo.DestroyObjectImmediate(r.gameObject);
            }
            foreach(var c in root.Find("Collisions").GetComponentsInChildren<BoxCollider2D>().ToArray())
            {
                if(c.name.Contains("edge") || c.name=="Entrance fence" || c.name=="Tree trunk" || c.name=="Water")continue;
                var p=c.bounds.center;
                if((p.x>-25 && p.x<-4 && p.y>-20.5f && p.y<-4) || (p.x>16 && p.x<27 && p.y>-5 && p.y<7) || Field.Contains(p))Undo.DestroyObjectImmediate(c.gameObject);
            }
        }
        static void Bed(float left,float bottom,int width,int height)
        {
            int x0=Mathf.RoundToInt(left*2),y0=Mathf.RoundToInt(bottom*2);
            for(int y=0;y<height*2;y++)for(int x=0;x<width*2;x++)
            {
                int sx=x==0?16:x==width*2-1?24:20,sy=y==0?424:y==height*2-1?416:420;
                soil.SetTile(new Vector3Int(x0+x,y0+y,0),AssetDatabase.LoadAssetAtPath<TileBase>($"Assets/Farm/Tiles/bed_soil_{sx}_{sy}.asset"));
            }
        }
        static void Fence(string name,float left,float bottom,float right,float top,bool eastGate,float gateMin,float gateMax,Transform parent)
        {
            var f=New(name,parent);
            foreach(float y in new[]{bottom,top})for(float x=left;x<right;x+=1.5f)
            {
                float px=Mathf.Min(x,right-2);
                if(!eastGate && y==top && px+2>gateMin && px<gateMax)continue;
                Place("Fence rail","fence_brown_horizontal",new Vector2(px,y-.5f),f);
                Box("Fence rail",new Vector2(px+1,y),new Vector2(1.5f,.25f),f);
            }
            foreach(float x in new[]{left,right})for(float y=bottom;y<top;y++)
            {
                if(eastGate && x==right && y>=gateMin && y<gateMax)continue;
                Place("Fence post","fence_brown_vertical",new Vector2(x-.25f,y),f);
                Box("Fence post",new Vector2(x,y+.5f),new Vector2(.25f,1),f);
            }
        }
        static void BuildFields()
        {
            soil.ClearAllTiles();Bed(-25.5f,3.5f,14,11);
            Fence("Larger planting field",-26,3,-11,15,true,9,11,revision);
            // Open flower beds remain buildable by the existing gardening tool.
            Bed(-6,7,1,4);Bed(3,7,1,4);soil.CompressBounds();
            var flowers=New("Cottage flower garden",revision);
            Place("Garden corner flowers","flower_box_yellow",new Vector2(-5.5f,11.5f),flowers);
            Place("Garden corner flowers","flower_box_pink",new Vector2(2.5f,11.5f),flowers);
            New("Flower planting strip west",flowers).position=new Vector3(-5.5f,9,0);
            New("Flower planting strip east",flowers).position=new Vector3(3.5f,9,0);
        }
        static void BuildBuildings()
        {
            foreach(var b in JsonUtility.FromJson<Plan>(File.ReadAllText("Assets/Farm/Design/settlement-layout.json")).buildings)
            {
                var group=New(b.name,root.Find("Buildings"));group.gameObject.AddComponent<SortingGroup>();FarmSortingEditor.SetSourceDepth(group.gameObject,Mathf.RoundToInt((24-b.bottom)*64));
                foreach(var p in b.parts){var r=Place(p.name,p.sprite,new Vector2(p.x,p.y),group);r.sortingOrder=1000+p.order;}
                foreach(var c in b.colliders)Box("Building footprint",new Vector2(c.x,c.y),new Vector2(c.w,c.h),group);
            }
        }
        static void BuildPasture()
        {
            var pasture=New("Southwest animal pasture",revision);
            Fence("Pasture fence",-24,-20,-7,-10,false,-17.5f,-14.5f,pasture);
            Place("Water trough","trough",new Vector2(-22,-12.5f),pasture);
            Place("Feed trough","trough",new Vector2(-19.5f,-12.5f),pasture);
            Place("Hay bale","hay_bale",new Vector2(-9,-12.5f),pasture);
            Place("Hay bale","hay_bale",new Vector2(-10,-12.5f),pasture);
            Place("Water barrel","barrel_water",new Vector2(-23,-15),pasture);
            foreach(var p in new[]{new Vector2(-20,-17),new Vector2(-15,-18),new Vector2(-11,-16),new Vector2(-18,-14)})Place("Pasture grass","grass_sparse",p,pasture);
            New("Pasture entrance",pasture).position=new Vector3(-16,-10,0);
        }
        static void BuildPier()
        {
            var pier=New("River pier",revision);
            for(int y=17;y<=21;y++)Place("Pier deck","dock_vertical_panel",new Vector2(-2,y),pier);
            foreach(float x in new[]{-4f,-2f,0f})Place("Pier head","dock_horizontal_panel",new Vector2(x,21),pier);
            Delete("Reference Landscape Revision/River water collision");
            var water=New("River collision around pier",pier);
            // Separate strips leave walkable decking above the water.
            for(int x=0;x<64;x++)
            {
                int depth=x<12 || (x>=27&&x<43) || x>=56?6:5;
                for(int y=0;y<depth;y++)
                {
                    float wx=x-31.5f,wy=23.5f-y;
                    bool deck=wx>=-2&&wx<0&&wy>=17&&wy<23 || wx>=-4&&wx<2&&wy>=21&&wy<23;
                    if(!deck)Box("River water",new Vector2(wx,wy),Vector2.one,water);
                }
            }
            Box("Pier left rail",new Vector2(-2.05f,19),new Vector2(.15f,4),pier);
            Box("Pier right rail",new Vector2(.05f,19),new Vector2(.15f,4),pier);
            Box("Pier head rail",new Vector2(-1,23),new Vector2(6,.15f),pier);
            Box("Pier head west rail",new Vector2(-4,22),new Vector2(.15f,2),pier);
            Box("Pier head east rail",new Vector2(2,22),new Vector2(.15f,2),pier);
        }
        static void RepairPier()
        {
            var pier=revision.Find("River pier");
            if(pier==null || pier.Find("Continuous deck")!=null)return;
            foreach(var r in pier.GetComponentsInChildren<SpriteRenderer>().Where(r=>r.name=="Pier deck").ToArray())Undo.DestroyObjectImmediate(r.gameObject);
            for(int y=17;y<=21;y++)Place("Pier deck","dock_vertical_panel",new Vector2(-2,y),pier);
            foreach(float x in new[]{-4f,-2f,0f})Place("Pier head infill","dock_horizontal_panel",new Vector2(x,21.5f),pier);
            New("Continuous deck",pier);
        }
        static void BuildTrees()
        {
            var parent=New("Expanded eastern woodland",revision);int count=0;
            var points=new List<Vector2>();
            foreach(float x in new[]{7.5f,13.5f,19.5f,25.5f})points.Add(new Vector2(x,14.25f));
            foreach(float x in new[]{7.5f,13.5f,19.5f,25.5f})points.Add(new Vector2(x,3));
            foreach(float y in new[]{0f,-3.5f})foreach(float x in new[]{17f,20.5f,24f,27f})points.Add(new Vector2(x,y));
            foreach(var p in new[]{new Vector2(4,-.5f),new Vector2(7,-.5f),new Vector2(11,-1),new Vector2(13.5f,-4),new Vector2(15,-7),new Vector2(12,-9),new Vector2(5,-9),new Vector2(5,-12),new Vector2(25,-7),new Vector2(27,-10)})points.Add(p);
            foreach(var p in points)
            {
                var foot=p+Vector2.up*.34375f;
                if(ground.GetSprite(ground.WorldToCell(foot))?.name!="ground_grass" || roads.HasTile(roads.WorldToCell(foot)))continue;
                if(root.GetComponentsInChildren<BoxCollider2D>().Any(c=>Vector2.Distance(c.bounds.center,foot)<1.15f))continue;
                var r=Place("Roadside tree",count++%4==3?"tree_pine":"tree_round",p+Vector2.left,parent);
                var c=r.gameObject.AddComponent<BoxCollider2D>();c.size=new Vector2(.5f,.4375f);c.offset=new Vector2(1,.34375f);
            }
            File.AppendAllText(Report,"Additional collidable eastern trees: "+count+"\n");
        }
        static void AddFlowerSeeds()
        {
            string path="Assets/Farm/Gardening/Crops.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            var factory=new SpriteDataProviderFactories();factory.Init();var provider=factory.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
            var rects=provider.GetSpriteRects().ToList();int height=AssetDatabase.LoadAssetAtPath<Texture2D>(path).height;
            void Add(string name,int y,int h,Vector2 pivot)
            {if(rects.Any(r=>r.name==name))return;rects.Add(new SpriteRect{name=name,rect=new Rect(256,height-y-h,16,h),alignment=SpriteAlignment.Custom,pivot=pivot,spriteID=GUID.Generate()});}
            Add("Rose_Packet",16,16,new Vector2(.5f,.5f));Add("Rose_Stage0",32,16,new Vector2(.5f,.5f));
            Add("Rose_Stage1",48,32,new Vector2(.5f,.25f));Add("Rose_Stage2",112,32,new Vector2(.5f,.25f));Add("Rose_Stage3",144,32,new Vector2(.5f,.25f));
            provider.SetSpriteRects(rects.ToArray());provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));provider.Apply();importer.SaveAndReimport();
            var map=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s=>s.name);
            const string cropPath="Assets/Farm/Gardening/Crops/Rose.asset";
            var crop=AssetDatabase.LoadAssetAtPath<FarmCropDefinition>(cropPath);
            if(crop==null){crop=ScriptableObject.CreateInstance<FarmCropDefinition>();AssetDatabase.CreateAsset(crop,cropPath);}
            crop.displayName="Розы";crop.seedPacket=map["Rose_Packet"];crop.stages=Enumerable.Range(0,4).Select(i=>map["Rose_Stage"+i]).ToArray();crop.growthSeconds=15;EditorUtility.SetDirty(crop);
            var garden=UnityEngine.Object.FindAnyObjectByType<FarmGardenSystem>();if(!garden.crops.Contains(crop)){garden.crops=garden.crops.Concat(new[]{crop}).ToArray();EditorUtility.SetDirty(garden);}
        }
        static void Validate()
        {
            Physics2D.SyncTransforms();
            var house=root.Find("Buildings/Yellow Cottage");var barn=root.Find("Buildings/Red Barn");
            if(house==null||barn==null||house.GetComponentsInChildren<BoxCollider2D>().Length!=2||barn.GetComponentsInChildren<BoxCollider2D>().Length!=1)throw new InvalidOperationException("Building collision missing.");
            var garden=UnityEngine.Object.FindAnyObjectByType<FarmGardenSystem>();
            var f=typeof(FarmGardenSystem).GetField("scenery",BindingFlags.Instance|BindingFlags.NonPublic);var old=f.GetValue(garden);int plots=0;
            try
            {
                f.SetValue(garden,garden.environment.GetComponentsInChildren<SpriteRenderer>(true));
                var tests=new List<Vector3>();
                for(int x=-24;x<=-13;x++)for(int y=5;y<=13;y++)tests.Add(new Vector3(x+.5f,y+.5f,0));
                foreach(float x in new[]{-5.5f,3.5f})for(int y=7;y<=10;y++)tests.Add(new Vector3(x,y+.5f,0));
                foreach(var p in tests)
                {if(Vector2.Distance(p,garden.player.position)<1.5f)continue;if(!garden.CanPlaceAt(garden.soil.WorldToCell(p),out var reason))throw new InvalidOperationException("Planting blocked at "+p+": "+reason);plots++;}
            }finally{f.SetValue(garden,old);}
            if(garden.crops.Length!=7||garden.crops.Last().stages.Any(s=>s==null))throw new InvalidOperationException("Flower seed sprites missing.");
            // Flood fill actual collision-free road cells, including all new branches.
            var clear=new HashSet<Vector3Int>();foreach(var p in roads.cellBounds.allPositionsWithin)
                if(roads.HasTile(p)&&!Physics2D.OverlapCircleAll(roads.GetCellCenterWorld(p),.22f).Any(c=>!c.isTrigger&&c.GetComponentInParent<FarmPlayerController>()==null))clear.Add(p);
            var seen=new HashSet<Vector3Int>();var q=new Queue<Vector3Int>();q.Enqueue(roads.WorldToCell(new Vector3(-1,-20,0)));
            while(q.Count>0){var p=q.Dequeue();if(!clear.Contains(p)||!seen.Add(p))continue;foreach(var d in new[]{Vector3Int.up,Vector3Int.down,Vector3Int.left,Vector3Int.right})q.Enqueue(p+d);}
            foreach(var t in new[]{new Vector3(-10.5f,10,0),new Vector3(26,10,0),new Vector3(-1,17.5f,0),new Vector3(-15.5f,-9.5f,0),new Vector3(8.5f,-9,0)})
                if(!seen.Any(p=>Vector3.Distance(roads.GetCellCenterWorld(p),t)<1))throw new InvalidOperationException("Disconnected route near "+t);
            foreach(var p in new[]{new Vector2(-16,-10),new Vector2(-1,18),new Vector2(-1,20),new Vector2(-1,22)})
                if(Physics2D.OverlapCircleAll(p,.25f).Any(c=>!c.isTrigger))throw new InvalidOperationException("Blocked gate/pier at "+p);
            if(root.GetComponentsInChildren<Transform>().Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))>0)throw new InvalidOperationException("Missing script.");
            File.AppendAllText(Report,$"PASS: {plots} vegetable/flower cells; 7 seeds with complete flower stages; both building footprints; connected roads; open pasture gate and river pier.\n");
        }
    }
}
