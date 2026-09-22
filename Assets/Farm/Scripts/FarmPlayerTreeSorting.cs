using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farm
{
    [DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
    public sealed class FarmPlayerTreeSorting : MonoBehaviour
    {
        [SerializeField] private int normalOrder = 100;
        [SerializeField, Min(0)] private float approachMargin = .6f;
        private SpriteRenderer player;
        private Collider2D playerCollider;
        private SpriteRenderer[] trees;
        private void Awake()
        {
            player=GetComponent<SpriteRenderer>();
            playerCollider=GetComponent<Collider2D>();
            trees=FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .Where(r=>r.sprite!=null && r.sprite.name.StartsWith("tree_")).ToArray();
        }
        private void LateUpdate()
        {
            int order=normalOrder;
            float playerFeet=playerCollider!=null?playerCollider.bounds.min.y:player.bounds.min.y;
            foreach(var tree in trees)
            {
                if(tree==null || !tree.enabled || !tree.gameObject.activeInHierarchy)continue;
                var bounds=tree.bounds;bounds.Expand(new Vector3(approachMargin*2,approachMargin*2,2));
                if(!bounds.Intersects(player.bounds))continue;
                var group=tree.GetComponentInParent<SortingGroup>();
                int treeOrder=group!=null?group.sortingOrder:tree.sortingOrder;
                var trunk=tree.GetComponent<Collider2D>();
                if(trunk==null && group!=null)trunk=group.GetComponentInChildren<Collider2D>();
                float trunkY=trunk!=null?trunk.bounds.center.y:tree.bounds.min.y+.2f;
                if(playerFeet<trunkY)order=Mathf.Max(order,treeOrder+1);
            }
            player.sortingOrder=order;
        }
        private void OnDisable(){if(player!=null)player.sortingOrder=normalOrder;}
    }
}
