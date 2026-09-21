using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farm
{
    // Half-cell navigation uses the same terrain and colliders as gameplay.
    public sealed class FarmPlotPathfinder
    {
        private readonly Tilemap ground;
        private readonly Transform player;
        private readonly Vector2 size, offset;
        private const float Step = .5f;
        private static readonly Vector2Int[] Neighbours = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        public FarmPlotPathfinder(Tilemap ground, Transform player)
        {
            this.ground = ground; this.player = player;
            var collider = player.GetComponent<Collider2D>();
            size = collider.bounds.size;
            offset = (Vector2)collider.bounds.center - (Vector2)player.position;
        }
        private bool Obstacle(Collider2D collider) => collider != null && !collider.isTrigger && !collider.transform.IsChildOf(player);
        private bool GroundAt(Vector2 position)
        {
            for (int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
            {
                var point = position + offset + Vector2.Scale(new Vector2(x,y), size*.5f);
                var sprite = ground.GetSprite(ground.WorldToCell(point));
                if(sprite == null || sprite.name != "ground_grass") return false;
            }
            return true;
        }
        public bool ClearSegment(Vector2 from, Vector2 to)
        {
            int samples = Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(from,to)/.2f));
            for(int i=0;i<=samples;i++) if(!GroundAt(Vector2.Lerp(from,to,i/(float)samples))) return false;
            foreach(var collider in Physics2D.OverlapBoxAll(to+offset,size,0)) if(Obstacle(collider)) return false;
            Vector2 delta = to-from;
            foreach(var hit in Physics2D.BoxCastAll(from+offset,size,0,delta.normalized,delta.magnitude)) if(Obstacle(hit.collider)) return false;
            return true;
        }
        public bool CanWorkFrom(Vector2 position, Vector2 target)
        {
            if(Vector2.Distance(position,target)>1.3f) return false;
            foreach(var hit in Physics2D.LinecastAll(position+offset,target)) if(Obstacle(hit.collider)) return false;
            return true;
        }
        public bool Find(Vector2 start, Vector2 target, out List<Vector2> route)
        {
            route = null;
            foreach(var obstacle in Physics2D.OverlapPointAll(target)) if(Obstacle(obstacle)) return false;
            var open = new List<Vector2Int>{Vector2Int.zero};
            var closed = new HashSet<Vector2Int>();
            var cost = new Dictionary<Vector2Int,float>{{Vector2Int.zero,0}};
            var parent = new Dictionary<Vector2Int,Vector2Int>();
            var valid = new Dictionary<Vector2Int,bool>();
            Vector2 World(Vector2Int node) => start + (Vector2)node*Step;
            float Estimate(Vector2Int node) => Mathf.Max(0, Vector2.Distance(World(node),target)-1.3f);
            while(open.Count>0 && closed.Count<12000)
            {
                int best=0;
                for(int i=1;i<open.Count;i++) if(cost[open[i]]+Estimate(open[i]) < cost[open[best]]+Estimate(open[best])) best=i;
                var current=open[best]; open.RemoveAt(best);
                if(!closed.Add(current)) continue;
                if(CanWorkFrom(World(current),target))
                {
                    route=new List<Vector2>();
                    while(parent.TryGetValue(current,out var previous)) { route.Add(World(current)); current=previous; }
                    route.Reverse(); return true;
                }
                foreach(var direction in Neighbours)
                {
                    var next=current+direction;
                    if(closed.Contains(next)) continue;
                    if(!valid.TryGetValue(next,out bool passable))
                    {
                        passable=GroundAt(World(next));
                        if(passable) foreach(var obstacle in Physics2D.OverlapBoxAll(World(next)+offset,size,0)) if(Obstacle(obstacle)) { passable=false; break; }
                        valid[next]=passable;
                    }
                    if(!passable || !ClearSegment(World(current),World(next))) continue;
                    float nextCost=cost[current]+Step;
                    if(cost.TryGetValue(next,out float known) && known<=nextCost) continue;
                    cost[next]=nextCost; parent[next]=current; open.Add(next);
                }
            }
            return false;
        }
    }
}
