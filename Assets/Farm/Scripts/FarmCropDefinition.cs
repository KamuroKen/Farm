using UnityEngine;

namespace Farm
{
    [CreateAssetMenu(menuName = "Farm/Crop")]
    public sealed class FarmCropDefinition : ScriptableObject
    {
        public string displayName;
        public Sprite seedPacket;
        public Sprite[] stages = new Sprite[4];
        [Min(3)] public float growthSeconds = 15;
        [Min(1)] public int harvestAmount = 1;
    }
}
