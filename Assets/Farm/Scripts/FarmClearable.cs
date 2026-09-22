using System.Collections.Generic;
using UnityEngine;

namespace Farm
{
    // Attached to the whole removable object, including its child sprites and colliders.
    public sealed class FarmClearable : MonoBehaviour
    {
        private static readonly HashSet<string> Allowed = new HashSet<string> {
            "Grass Tuft", "Grass Sparse", "Clover Patch", "Flowers Purple", "Flowers Yellow",
            "Flowers White", "Flowers Pink", "Bush Leafy", "Bush Dense", "Bush Round",
            "Rock Blue Small", "Rock Blue Medium", "Pebble Pair", "Mushrooms"
        };
        public static void Register(Transform environment)
        {
            foreach(var item in environment.GetComponentsInChildren<Transform>(true))
                if(Allowed.Contains(item.name) && item.GetComponentInParent<FarmClearable>() == null)
                    item.gameObject.AddComponent<FarmClearable>();
        }
        private Dictionary<string,int> loot;
        public IReadOnlyDictionary<string,int> Loot(FarmGardenSystem garden)
        {
            if(loot != null) return loot;
            string item=name.StartsWith("Bush") ? "twig" : name.StartsWith("Rock") || name=="Pebble Pair" ? "stone" : name=="Mushrooms" ? "mushroom" : name.StartsWith("Flowers") ? "flower" : "fiber";
            loot=new Dictionary<string,int>{{item,1}};
            if((item=="fiber" || item=="flower") && Random.value<.25f)
                loot.Add(garden.SeedId(Random.Range(0,garden.crops.Length)),1);
            return loot;
        }
        public Bounds VisualBounds
        {
            get {
                var bounds=new Bounds(transform.position,Vector3.zero); bool first=true;
                foreach(var renderer in GetComponentsInChildren<SpriteRenderer>())
                    if(renderer.enabled && renderer.sprite!=null) { if(first) {bounds=renderer.bounds;first=false;} else bounds.Encapsulate(renderer.bounds); }
                return bounds;
            }
        }
        public Vector2 Center => VisualBounds.center;
        public float WorkingDistance => Mathf.Max(1f,Mathf.Max(VisualBounds.extents.x,VisualBounds.extents.y)+.55f);
        public bool Available => isActiveAndEnabled && gameObject.activeInHierarchy;
        public static FarmClearable Pick(Transform environment,Vector2 point)
        {
            FarmClearable best=null; int order=int.MinValue;
            foreach(var item in environment.GetComponentsInChildren<FarmClearable>())
            {
                if(!item.Available) continue;
                foreach(var renderer in item.GetComponentsInChildren<SpriteRenderer>())
                {
                    var b=renderer.bounds;
                    if(!renderer.enabled || renderer.sprite==null || point.x<b.min.x || point.x>b.max.x || point.y<b.min.y || point.y>b.max.y) continue;
                    if(renderer.sortingOrder>=order) {order=renderer.sortingOrder;best=item;}
                }
            }
            return best;
        }
    }
}
