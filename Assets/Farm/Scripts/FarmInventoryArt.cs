using System.Collections.Generic;
using UnityEngine;
namespace Farm
{
    public sealed class FarmInventoryArt
    {
        private readonly List<Sprite> sprites=new List<Sprite>();
        private readonly Texture2D inventory=Resources.Load<Texture2D>("FarmInventory/Inventory");
        private readonly Texture2D icons=Resources.Load<Texture2D>("FarmInventory/Icons");
        public Sprite Panel => Slice(inventory,8,0,96,101,new Vector4(6,6,6,15));
        public Sprite Slot => Slice(inventory,128,16,16,16,new Vector4(1,1,1,1));
        public Sprite Backpack => Icon(3,0);
        public Sprite HarvestIcon(Sprite packet) => Slice(packet.texture,(int)packet.rect.x,208,16,16,Vector4.zero);
        public Sprite Icon(int column,int row) => Slice(icons,column*16,row*16,16,16,Vector4.zero);
        private Sprite Slice(Texture2D texture,int x,int top,int width,int height,Vector4 border)
        {
            if(texture==null) throw new System.InvalidOperationException("Inventory UI assets are missing. Run Tools/Farm/Import Inventory UI.");
            var sprite=Sprite.Create(texture,new Rect(x,texture.height-top-height,width,height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,border);
            sprites.Add(sprite);return sprite;
        }
        public void Dispose(){foreach(var sprite in sprites) Object.Destroy(sprite);}
    }
}
