using System;
using UnityEngine;

namespace Trainer.Unity.World.Expedition
{
    /// <summary>Bounded renderer pool over a world-anchored grid. Never moves crew or changes authority coordinates.</summary>
    public sealed class LunarSurfaceView : MonoBehaviour
    {
        public const double TileWidth = 24, TileHeight = 16;
        private readonly SpriteRenderer[] tiles = new SpriteRenderer[49];
        private Camera target;
        private long previousX = long.MinValue, previousY = long.MinValue;
        public static long Column(double x) => (long)Math.Floor(x / TileWidth);
        public static long Row(double y) => (long)Math.Floor(y / TileHeight);
        public static Vector2 CellOrigin(long x, long y) => new Vector2((float)(x * TileWidth), (float)(y * TileHeight));
        public void Configure(Sprite sprite, Material material)
        {
            target = Camera.main;
            for (int i=0;i<tiles.Length;i++)
            {
                var go=new GameObject("Lunar ground tile");go.transform.SetParent(transform,false);
                var r=go.AddComponent<SpriteRenderer>();r.sprite=sprite;r.sharedMaterial=material;r.sortingOrder=-1200;
                go.transform.localScale=new Vector3((float)TileWidth/sprite.bounds.size.x,(float)TileHeight/sprite.bounds.size.y,1);
                tiles[i]=r;
            }
            Recenter(0,0);
        }
        private void LateUpdate()
        {
            if(target==null) target=Camera.main;
            if(target!=null) Recenter(target.transform.position.x,target.transform.position.y);
        }
        public void Recenter(double x,double y)
        {
            long cx=Column(x),cy=Row(y);
            if(cx==previousX && cy==previousY)return;
            previousX=cx;previousY=cy;
            int i=0;
            for(long row=cy-3;row<=cy+3;row++)
                for(long col=cx-3;col<=cx+3;col++)
                {
                    Vector2 origin=CellOrigin(col,row);
                    var tile=tiles[i++];if(tile==null) continue;
                    // Mirrored neighbours share exactly the same source edge; parity is global, never per client.
                    tile.flipX=(col & 1)!=0;tile.flipY=(row & 1)!=0;
                    tile.transform.position=new Vector3(origin.x+(float)TileWidth/2,origin.y+(float)TileHeight/2,0);
                }
        }
    }
}
