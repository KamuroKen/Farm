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
    public static class FarmShorePolishEditor
    {
        [Serializable] class Layout { public Layer[] layers; }
        [Serializable] class Layer { public string name; public Cell[] tiles; }
        [Serializable] class Cell { public string sprite; public int x,y; }
        const string Request="Library/FarmShore.request";
        static FarmShorePolishEditor(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
            var command=File.ReadAllText(Request).Trim();File.Delete(Request);
            try{if(command=="rotate")RotateShore();else Run(command=="apply");}catch(Exception ex){File.WriteAllText("BuildArtifacts/shore-polish-error.txt",ex.ToString());Debug.LogException(ex);}
        }
        static void RotateShore()
        {
            const string path="Assets/Scenes/FarmLevel.unity";
            var scene=SceneManager.GetSceneByPath(path);
            if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var root=scene.GetRootGameObjects().First(g=>g.name=="Farm Environment").transform;
            var shore=root.Find("Terrain/02 Shoreline/Tiles").GetComponent<Tilemap>();
            var edge=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Farm/Tiles/shore_144_120.asset");
            var corner=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Farm/Tiles/shore_144_96.asset");
            if(edge==null||corner==null)throw new IOException("Missing canonical shore tiles.");
            var rules=new Dictionary<string,(TileBase tile,int angle)>();
            foreach(var y in new[]{120,128}){rules[$"shore_144_{y}"]=(edge,0);rules[$"shore_200_{y}"]=(edge,180);}
            foreach(var x in new[]{176,184}){rules[$"shore_{x}_96"]=(edge,-90);rules[$"shore_{x}_152"]=(edge,90);}
            rules["shore_144_96"]=(corner,0);
            rules["shore_200_96"]=(corner,-90);
            rules["shore_200_152"]=(corner,180);
            rules["shore_144_152"]=(corner,90);
            var backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeRotatedShore.unity");
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Could not back up shoreline scene.");
            int changed=0;
            foreach(var p in shore.cellBounds.allPositionsWithin)
            {
                var name=shore.GetSprite(p)?.name;
                if(name==null||!rules.TryGetValue(name,out var rule))continue;
                shore.SetTile(p,rule.tile);
                shore.SetTileFlags(p,TileFlags.None);
                shore.SetTransformMatrix(p,Matrix4x4.Rotate(Quaternion.Euler(0,0,rule.angle)));
                changed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save rotated shoreline.");
            File.WriteAllText("BuildArtifacts/shore-rotation-report.txt",$"Backup: {backup}\nRotated canonical side/corner sprites across {changed} shore cells.\n");
        }
        static void Run(bool apply)
        {
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/FarmLevel.unity");
            if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/FarmLevel.unity",OpenSceneMode.Additive);
            var root=scene.GetRootGameObjects().First(g=>g.name=="Farm Environment").transform;
            var ground=root.Find("Terrain/01 Grass and water/Tiles").GetComponent<Tilemap>();
            var shore=root.Find("Terrain/02 Shoreline/Tiles").GetComponent<Tilemap>();
            var lines=new List<string>();
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var loaded=SceneManager.GetSceneAt(i);
                lines.Add($"Loaded scene {loaded.path} roots={string.Join(",",loaded.GetRootGameObjects().Select(g=>g.name).Take(8))}");
            }
            foreach(var region in new[]{new{label="lake",x0=4,x1=29,y0=-21,y1=-7},new{label="river",x0=-8,x1=8,y0=16,y1=24}})
            {
                lines.Add(region.label);
                for(int y=region.y1;y>=region.y0;y--)
                {
                    var row=$"{y,3} ";for(int x=region.x0;x<=region.x1;x++)row+=ground.GetSprite(new Vector3Int(x,y,0))?.name=="ground_water"?'~':'#';
                    lines.Add(row);
                }
            }
            var groups=scene.GetRootGameObjects().Where(g=>g.name.StartsWith("shore_")).Select(g=>g.name).ToArray();
            lines.Add("Stray shoreline objects: "+string.Join(",",groups));
            foreach(var obj in scene.GetRootGameObjects().Where(g=>g!=root.gameObject&&g.name!="Player - Bunny"&&g.name!="Gardening"))
            {
                foreach(var r in obj.GetComponentsInChildren<SpriteRenderer>(true))lines.Add($"Stray root sprite {obj.name}/{r.name} {r.sprite?.name} bounds={r.bounds} order={r.sortingOrder}");
            }
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SpriteRenderer>(true)).Where(r=>r.sprite!=null&&r.sprite.name.StartsWith("shore_")))
                lines.Add($"Shore SpriteRenderer {r.name} {r.sprite.name} at {r.bounds.center} order={r.sortingOrder}");
            lines.Add("Shore tile count: "+shore.GetUsedTilesCount());
            lines.Add($"Ground transform={ground.transform.position} cellSize={ground.layoutGrid.cellSize} anchor={ground.tileAnchor} sample={ground.GetCellCenterWorld(new Vector3Int(9,-12,0))}");
            lines.Add($"Shore transform={shore.transform.position} cellSize={shore.layoutGrid.cellSize} anchor={shore.tileAnchor} sample={shore.GetCellCenterWorld(new Vector3Int(18,-24,0))}");
            foreach(var m in root.GetComponentsInChildren<Tilemap>())lines.Add($"Tilemap {m.transform.parent.name}: world={m.transform.position} local={m.transform.localPosition} scale={m.transform.lossyScale} parent={m.transform.parent.position} cell={m.layoutGrid.cellSize}");
            foreach(var m in root.GetComponentsInChildren<Tilemap>().Where(m=>m!=ground&&m!=shore))
            {
                var spriteCounts=new Dictionary<string,int>();
                var submerged=new List<string>();
                foreach(var p in m.cellBounds.allPositionsWithin)if(m.HasTile(p))
                {
                    var n=m.GetSprite(p)?.name??"?";spriteCounts[n]=spriteCounts.GetValueOrDefault(n)+1;
                    if(ground.GetSprite(ground.WorldToCell(m.GetCellCenterWorld(p)))?.name=="ground_water")submerged.Add($"{p.x},{p.y}:{n}");
                }
                lines.Add($"Overlay {m.transform.parent.name} sprite counts: {string.Join(" ",spriteCounts.OrderByDescending(kv=>kv.Value).Take(8).Select(kv=>kv.Key+":"+kv.Value))}");
                lines.Add($"Overlay {m.transform.parent.name} in water: {submerged.Count} {string.Join(" ",submerged.Take(30))}");
                lines.Add($"Overlay {m.transform.parent.name} visible in water: {string.Join(" ",submerged.Where(x=>!x.EndsWith(":ground_water")).Take(60))}");
            }
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>())
                if(r.sprite!=null&&ground.GetSprite(ground.WorldToCell(r.bounds.center))?.name=="ground_water")lines.Add($"Renderer in water {r.name} {r.sprite.name} {r.bounds.center} order={r.sortingOrder}");
            var probe=new Bounds(new Vector3(13,-10.5f,0),new Vector3(6,3,2));
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>())if(r.bounds.Intersects(probe))lines.Add($"Renderer near north lake {r.name} {r.sprite?.name} {r.bounds}");
            var roadMap=root.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
            foreach(var point in new[]{new Vector2(12.5f,-10.5f),new Vector2(13f,-10.5f),new Vector2(14f,-10.5f),new Vector2(13f,-9.5f),new Vector2(13f,-11.5f)})
                lines.Add($"Sample {point}: ground={ground.GetSprite(ground.WorldToCell(point))?.name} shore={shore.GetSprite(shore.WorldToCell(point))?.name} road={roadMap.GetSprite(roadMap.WorldToCell(point))?.name}");
            foreach(var r in root.GetComponentsInChildren<Renderer>().Where(r=>!(r is SpriteRenderer)&&!(r is TilemapRenderer)))lines.Add($"Other renderer {r.GetType().Name} {r.name} {r.bounds}");
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/Farm/Design/landscape-layout.json"));
            var expected=layout.layers.First(l=>l.name=="02 Shoreline").tiles.ToDictionary(t=>new Vector3Int(t.x,t.y,0),t=>t.sprite);
            int missing=0,extra=0,changed=0;
            foreach(var pair in expected)
            {
                var current=shore.GetSprite(pair.Key)?.name;
                if(current==null)missing++;else if(current!=pair.Value)changed++;
            }
            foreach(var p in shore.cellBounds.allPositionsWithin)if(shore.HasTile(p)&&!expected.ContainsKey(p))extra++;
            lines.Add($"Compared with original shoreline: {missing} missing, {extra} extra, {changed} changed");
            int transformed=0;foreach(var p in shore.cellBounds.allPositionsWithin)if(shore.HasTile(p)&&shore.GetTransformMatrix(p)!=Matrix4x4.identity)transformed++;
            lines.Add("Shore tiles with custom transform: "+transformed);
            Directory.CreateDirectory("BuildArtifacts");File.WriteAllLines("BuildArtifacts/shore-inspection.txt",lines);
            if(!apply)return;
            var backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/Backups/FarmLevel_BeforeShoreAlignment.unity");
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Could not back up the scene.");
            ground.transform.localPosition=Vector3.zero;
            var roads=root.Find("Terrain/03 Earthen paths/Tiles").GetComponent<Tilemap>();
            int overlays=0;
            foreach(var p in roads.cellBounds.allPositionsWithin)
            {
                var sprite=roads.GetSprite(p);
                if(sprite==null || (sprite.name!="ground_water"&&!sprite.name.StartsWith("shore_")))continue;
                roads.SetTile(p,null);overlays++;
            }
            roads.CompressBounds();
            foreach(var obj in scene.GetRootGameObjects().Where(g=>(g.name.StartsWith("shore_")||g.name=="path_256_16"||g.name=="Autotile_Grass_and_Dirt_Path_Tileset_0")&&g.GetComponent<SpriteRenderer>()!=null).ToArray())
                UnityEngine.Object.DestroyImmediate(obj);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save aligned shoreline.");
            File.WriteAllText("BuildArtifacts/shore-polish-report.txt",$"Backup: {backup}\nAligned ground and shoreline tilemaps at the same origin; removed stray shoreline/path sprites and {overlays} misplaced water/shore tiles from the road layer.\n");
        }
    }
}
