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
    /// <summary>Applies the second reference revision to the saved farm, retaining unrelated edits.</summary>
    public static class FarmLandscapeEditor
    {
        [Serializable] private class Layout { public Layer[] layers; }
        [Serializable] private class Layer { public string name; public Cell[] tiles; }
        [Serializable] private class Cell { public string sprite; public int x,y; }
        private static Transform root;
        private static Tilemap ground, roads;
        private static Dictionary<string,Sprite> sprites;
        private static Material material;
        private static readonly Bounds Field = new Bounds(new Vector3(-13.5f,8,0),new Vector3(11,10,2));
        private const string ScenePath = "Assets/Scenes/FarmLevel.unity";
        private const string Report = "BuildArtifacts/landscape-validation.txt";

        [MenuItem("Tools/Farm/Apply River and Tree Avenues")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            root = scene.GetRootGameObjects().First(g=>g.name=="Farm Environment").transform;
            ground = root.Find("Terrain/01 Grass and water/Tiles").GetComponent<Tilemap>();
            roads = root.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
            Directory.CreateDirectory("BuildArtifacts");
            if (root.Find("Reference Landscape Revision") != null)
            {
                Polish();
                FarmSortingEditor.Normalize(scene);
                Validate();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save revised farm.");
                AssetDatabase.SaveAssets();
                EditorApplication.delayCall += Capture;
                return;
            }
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeLandscape.unity");
            if (!EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Cannot back up scene.");
            File.WriteAllText(Report,"Backup: "+backup+"\n");
            sprites = AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Farm/Atlases"})
                .SelectMany(g=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<Sprite>())
                .GroupBy(s=>s.name).ToDictionary(g=>g.Key,g=>g.First());
            material=roads.GetComponent<TilemapRenderer>().sharedMaterial;
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/Farm/Design/landscape-layout.json"));
            foreach(var layer in layout.layers)
            {
                var map=root.Find("Terrain/"+layer.name+"/Tiles").GetComponent<Tilemap>();
                var tiles=layer.tiles.Select(t=>AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Farm/Tiles/"+t.sprite+".asset")).ToArray();
                if(tiles.Any(t=>t==null)) throw new InvalidOperationException("Missing terrain tile.");
                Undo.RegisterCompleteObjectUndo(map,"Revise farm landscape");
                map.ClearAllTiles(); map.SetTiles(layer.tiles.Select(t=>new Vector3Int(t.x,t.y,0)).ToArray(),tiles); map.CompressBounds();
            }
            // Move water-side art, the dock, and its existing row colliders together.
            foreach(var path in new[]{"Pond","Collisions/Pond"})
            {
                var parent=root.Find(path);
                if(parent!=null) { Undo.RecordObject(parent,"Lower lake"); parent.position+=Vector3.down*3; }
            }
            var jetty=root.Find("Layout Markers/Pond Jetty");
            if(jetty!=null) jetty.position+=Vector3.down*3;
            var marker=root.Find("Layout Markers/North Exit — woodland trail");
            if(marker!=null) { marker.name="North River Bank"; marker.position=new Vector3(0,17,0); }
            var revision=New("Reference Landscape Revision",root);
            ClearScenery();
            // The cottage was removed before this revision; its invisible footprint must not block farming.
            var cottage=root.Find("Collisions/Buildings/Cottage footprint");
            if(cottage!=null && root.Find("Buildings/Cottage").GetComponentsInChildren<SpriteRenderer>().Length==0)
                Undo.DestroyObjectImmediate(cottage.gameObject);
            BuildRiverCollision(revision);
            BuildField(revision);
            BuildAvenues(revision);
            Polish();
            FarmSortingEditor.Normalize(scene);
            var fieldMap=root.Find("Terrain/06 Planting ground/Tiles").GetComponent<TilemapRenderer>();
            fieldMap.sortingOrder=12;
            Validate();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save revised farm.");
            AssetDatabase.SaveAssets();
            File.AppendAllText(Report,"Saved: "+ScenePath+"\n");
            EditorApplication.delayCall+=Capture;
        }
        static Transform New(string name,Transform parent)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            Undo.RegisterCreatedObjectUndo(go,"Revise farm landscape");return go.transform;
        }
        static SpriteRenderer Place(string name,string sprite,Vector2 position,Transform parent)
        {
            var t=New(name,parent);t.position=position;
            var r=t.gameObject.AddComponent<SpriteRenderer>();r.sprite=sprites[sprite];r.sharedMaterial=material;
            r.spriteSortPoint=SpriteSortPoint.Pivot;
            FarmSortingEditor.SetSourceDepth(t.gameObject,Mathf.RoundToInt((24-position.y)*64));
            return r;
        }
        static BoxCollider2D Box(string name,Vector2 center,Vector2 size,Transform parent)
        {
            var t=New(name,parent);t.position=center;var c=t.gameObject.AddComponent<BoxCollider2D>();c.size=size;return c;
        }
        static bool Water(Vector3 p) => ground.GetSprite(ground.WorldToCell(p))?.name=="ground_water";
        static void ClearScenery()
        {
            int trees=0, details=0;
            var treeColliders=root.Find("Collisions/Trees").GetComponentsInChildren<BoxCollider2D>().ToList();
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>().ToArray())
            {
                if(r.sprite==null || r.transform.IsChildOf(root.Find("Pond"))) continue;
                var b=r.bounds;bool tree=r.sprite.name.StartsWith("tree_");
                var foot=new Vector3(b.center.x,b.min.y+.34375f,0);
                bool remove=Field.Intersects(b) || Water(b.center) || (tree && (b.max.y>17.9f || Water(foot) || roads.HasTile(roads.WorldToCell(foot))));
                if(!tree && roads.HasTile(roads.WorldToCell(b.center)) && r.transform.IsChildOf(root.Find("Nature/Ground cover"))) remove=true;
                if(!remove) continue;
                if(tree)
                {
                    var c=treeColliders.Where(c=>c!=null).OrderBy(c=>Vector2.Distance(c.bounds.center,foot)).FirstOrDefault();
                    if(c!=null && Vector2.Distance(c.bounds.center,foot)<.65f) {treeColliders.Remove(c);Undo.DestroyObjectImmediate(c.gameObject);}
                    trees++;
                }
                else details++;
                Undo.DestroyObjectImmediate(r.gameObject);
            }
            File.AppendAllText(Report,$"Cleared {trees} trees with matching trunk colliders and {details} decorations from river/field.\n");
        }
        static void BuildRiverCollision(Transform parent)
        {
            var collisions=New("River water collision",parent);
            for(int x=0;x<64;)
            {
                int start=x, depth=x<12 || (x>=27 && x<43) || x>=56 ? 6:5;
                do{x++;}while(x<64 && (x<12 || (x>=27 && x<43) || x>=56 ? 6:5)==depth);
                Box("River water",new Vector2((start+x)/2f-32,24-depth/2f),new Vector2(x-start,depth),collisions);
            }
        }
        static void BuildField(Transform parent)
        {
            var grid=New("06 Planting ground",root.Find("Terrain"));grid.gameObject.AddComponent<Grid>().cellSize=new Vector3(.5f,.5f,1);
            var t=New("Tiles",grid);var map=t.gameObject.AddComponent<Tilemap>();var renderer=t.gameObject.AddComponent<TilemapRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=12;
            for(int y=7;y<25;y++) for(int x=-37;x<-17;x++)
            {
                int sx=x==-37?16:x==-18?24:20;
                int sy=y==24?416:y==7?424:420;
                map.SetTile(new Vector3Int(x,y,0),AssetDatabase.LoadAssetAtPath<TileBase>($"Assets/Farm/Tiles/bed_soil_{sx}_{sy}.asset"));
            }
            map.CompressBounds();
            var fences=New("Planting field fence",parent);
            foreach(float y in new[]{3f,13f}) for(float x=-19;x<-8;x+=1.5f)
            {
                // Final panel is fitted by overlap, as with the existing garden fencing.
                float px=Mathf.Min(x,-10f);
                Place("Field fence rail","fence_brown_horizontal",new Vector2(px,y-.5f),fences);
                Box("Fence rail",new Vector2(px+1,y),new Vector2(1.5f,.25f),fences);
            }
            foreach(float x in new[]{-19f,-8f}) for(int y=3;y<13;y++)
            {
                if(x==-8 && (y==9 || y==10))continue;
                Place("Field fence post","fence_brown_vertical",new Vector2(x-.25f,y),fences);
                Box("Fence post",new Vector2(x,y+.5f),new Vector2(.25f,1),fences);
            }
            New("Planting field entrance",parent).position=new Vector3(-8,10,0);
        }
        static void BuildAvenues(Transform parent)
        {
            var avenue=New("Roadside trees",parent);
            int index=0;
            Action<float,float> tree=(x,y)=>
            {
                string sprite=index++%4==3?"tree_pine":"tree_round";
                var r=Place("Avenue tree",sprite,new Vector2(x-33,24-y),avenue);
                // Exactly the Woodland footprint: 8 x 7 pixels, raised 5.5 pixels from the sprite base.
                var c=r.gameObject.AddComponent<BoxCollider2D>();c.size=new Vector2(.5f,.4375f);c.offset=new Vector2(1,.34375f);
            };
            foreach(float x in new[]{38f,41f,44f,47f,50f,53f,56f}) {tree(x,11.75f);tree(x,18f);}
            foreach(float x in new[]{34f,37f,40f}) tree(x,24.5f);
            foreach(float x in new[]{34f,37f}) tree(x,31f);
            tree(38,34);tree(43.5f,29.5f);tree(44.5f,32.5f);
            File.AppendAllText(Report,$"Added {index} trees with Woodland-sized trunk colliders.\n");
        }
        static void Polish()
        {
            var marker=root.Find("Reference Landscape Revision/Verge cleanup");
            if(marker!=null)return;
            int supply=0;
            var positions=new List<Vector3>();
            foreach(var p in roads.cellBounds.allPositionsWithin) if(roads.HasTile(p))positions.Add(roads.GetCellCenterWorld(p));
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>().ToArray())
            {
                if(r.sprite==null || r.sprite.name.StartsWith("tree_") || !positions.Any(p=>r.bounds.Contains(p)))continue;
                if(r.transform.IsChildOf(root.Find("Barnyard/Supplies")))
                {
                    Undo.RecordObject(r.transform,"Clear avenue supplies");
                    r.transform.position=new Vector3(19+(supply%4)*1.5f,1+(supply/4)*2,0);supply++;
                }
                else if(r.transform.IsChildOf(root.Find("Nature/Landmarks")))
                {
                    Undo.RecordObject(r.transform,"Clear avenue rocks");
                    r.transform.position+=Vector3.up*5;
                }
                else if(r.sprite.name.StartsWith("bush_") || r.transform.IsChildOf(root.Find("Nature/Ground cover")))Undo.DestroyObjectImmediate(r.gameObject);
            }
            New("Verge cleanup",root.Find("Reference Landscape Revision"));
        }
        static void Validate()
        {
            Physics2D.SyncTransforms();
            var avenue=root.Find("Reference Landscape Revision/Roadside trees");
            var trees=avenue.GetComponentsInChildren<SpriteRenderer>();
            if(trees.Length!=22 || trees.Any(r=>r.GetComponent<BoxCollider2D>()==null)) throw new InvalidOperationException("Tree collision count mismatch.");
            foreach(var r in trees)
            {
                var c=r.GetComponent<BoxCollider2D>();
                if(c.size!=new Vector2(.5f,.4375f) || c.isTrigger || Water(c.bounds.center)) throw new InvalidOperationException("Invalid avenue trunk collision.");
            }
            var clear=new HashSet<Vector3Int>();
            foreach(var p in roads.cellBounds.allPositionsWithin)
                if(roads.HasTile(p) && !Physics2D.OverlapCircleAll(roads.GetCellCenterWorld(p),.22f).Any(c=>!c.isTrigger && c.GetComponentInParent<FarmPlayerController>()==null))clear.Add(p);
            var seen=new HashSet<Vector3Int>();var todo=new Queue<Vector3Int>();todo.Enqueue(roads.WorldToCell(new Vector3(-1,-20,0)));
            while(todo.Count>0)
            {
                var p=todo.Dequeue();if(!clear.Contains(p)||!seen.Add(p))continue;
                foreach(var d in new[]{Vector3Int.up,Vector3Int.down,Vector3Int.left,Vector3Int.right})todo.Enqueue(p+d);
            }
            foreach(var target in new[]{new Vector3(-7.5f,10,0),new Vector3(26,10,0),new Vector3(-1,13,0),new Vector3(-7,-3,0),new Vector3(8.5f,-9,0)})
                if(!seen.Any(p=>Vector3.Distance(roads.GetCellCenterWorld(p),target)<1))throw new InvalidOperationException("Road blocked near "+target);
            for(int x=-17;x<=-10;x++)for(int y=5;y<=11;y++)
                if(Physics2D.OverlapBoxAll(new Vector2(x+.5f,y+.5f),Vector2.one*.88f,0).Any(c=>!c.isTrigger && c.GetComponentInParent<FarmPlayerController>()==null))throw new InvalidOperationException("Planting area blocked.");
            if(Physics2D.OverlapCircleAll(new Vector2(-8,10),.3f).Any(c=>!c.isTrigger))throw new InvalidOperationException("Field entrance blocked.");
            if(root.GetComponentsInChildren<Transform>().Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))!=0)throw new InvalidOperationException("Missing script.");
            var garden=root.GetComponentInChildren<FarmGardenSystem>();
            if(garden==null) garden=UnityEngine.Object.FindAnyObjectByType<FarmGardenSystem>();
            var sceneryField=typeof(FarmGardenSystem).GetField("scenery",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var previousScenery=sceneryField.GetValue(garden);
            int plantingChecks=0;
            try
            {
                sceneryField.SetValue(garden,garden.environment.GetComponentsInChildren<SpriteRenderer>(true));
                for(int x=-17;x<=-10;x++) for(int y=5;y<=11;y++)
                {
                    var point=new Vector3(x+.5f,y+.5f,0);
                    if(Vector2.Distance(point,garden.player.position)<1.5f)continue;
                    var cell=garden.soil.WorldToCell(point);
                    if(!garden.CanPlaceAt(cell,out var reason))throw new InvalidOperationException("Cannot farm at "+point+": "+reason);
                    plantingChecks++;
                }
            }
            finally { sceneryField.SetValue(garden,previousScenery); }
            File.AppendAllText(Report,"PASS: roads connected, field interior and entrance clear, all 22 trees collidable, no missing scripts.\n"+"PASS: "+plantingChecks+" field cells accepted by the actual gardening placement rules.\n");
        }
        static void Capture()
        {
            FarmRoadEditor.Capture();
            File.Copy("BuildArtifacts/FarmLevel-Roads.png","BuildArtifacts/FarmLevel-Landscape.png",true);
            File.WriteAllText("BuildArtifacts/landscape-complete.txt",DateTime.UtcNow.ToString("o"));
        }
    }
}
