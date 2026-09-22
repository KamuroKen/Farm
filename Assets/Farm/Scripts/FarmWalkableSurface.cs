using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farm
{
    /// <summary>Visible dock decking is traversable even when the terrain below is water.</summary>
    [DisallowMultipleComponent]
    public sealed class FarmWalkableSurface : MonoBehaviour
    {
        private static readonly HashSet<FarmWalkableSurface> Active=new HashSet<FarmWalkableSurface>();
        private SpriteRenderer[] panels;
        private void OnEnable(){Refresh();Active.Add(this);}
        private void OnDisable(){Active.Remove(this);}
        public void Refresh(){panels=GetComponentsInChildren<SpriteRenderer>().Where(r=>r.sprite!=null&&r.sprite.name.StartsWith("dock_")).ToArray();}
        public bool Contains(Vector2 point)
        {
            // During scene deserialization OnEnable can run before the child panels exist.
            if(panels==null || panels.Length==0)Refresh();
            foreach(var panel in panels)
            {
                if(panel==null||!panel.enabled)continue;
                var b=panel.bounds;
                // Keep the whole deck open so the player's collider can cross its landward edge.
                if(point.x>=b.min.x && point.x<b.max.x && point.y>=b.min.y && point.y<b.max.y)return true;
            }
            return false;
        }
        public static bool ContainsWorldPoint(Vector2 point)
        {foreach(var surface in Active)if(surface!=null&&surface.Contains(point))return true;return false;}
    }
}
