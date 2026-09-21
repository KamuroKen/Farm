using UnityEngine;

namespace Farm
{
    // Remembers the original composition across repeated editor normalization.
    // This component does no per-frame work; rendering uses the SpriteRenderer/SortingGroup order.
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class FarmSortingDepth : MonoBehaviour
    {
        [SerializeField, HideInInspector] private int sourceOrder;
        public int SourceOrder { get => sourceOrder; set => sourceOrder = value; }
    }
}
