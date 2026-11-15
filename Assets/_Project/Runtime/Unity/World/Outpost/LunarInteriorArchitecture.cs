using Trainer.Application.Outpost;
using UnityEngine;
using System.Linq;
using Trainer.Unity.Configuration;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>Modular visual skin drawn from the authority's rectangles. No movement or resource state.</summary>
    public sealed class LunarInteriorArchitecture : MonoBehaviour
    {
        private Texture2D gridTexture;
        private Sprite grid, white;
        private Material surfaceMaterial;
        public void Configure(OutpostScenario scenario,Material material)
        {
            surfaceMaterial=material;
            gridTexture=new Texture2D(128,128,TextureFormat.RGBA32,false) {name="Grey metal grid",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
            var pixels=new Color32[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                // User palette: grout #2B2C2E, tile faces #54565A.
                pixels[y*128+x]=x%64==0||y%64==0 ? new Color32(43,44,46,255) : new Color32(84,86,90,255);
            }
            gridTexture.SetPixels32(pixels);gridTexture.Apply(false,true);
            grid=Sprite.Create(gridTexture,new Rect(0,0,128,128),new Vector2(.5f,.5f),64,0,SpriteMeshType.FullRect);
            white=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1,0,SpriteMeshType.FullRect);
            TileFloor("Habitat grey grid",LunarBasePlan.Habitat);
            TileFloor("Airlock grey grid",LunarBasePlan.Chamber);
            foreach(var r in scenario.Layout.Obstacles)
            {
                if(r.MinX>=LunarBasePlan.OuterHatch.MaxX)continue;
                if(LunarBasePlan.Furniture.Any(f=>f.Equals(r)))
                {
                    Solid("Bench or storage footprint",r,new Color(.10f,.14f,.18f),-800);
                    Solid("Work surface",new BaseRect(r.MinX+.12,r.MinY+.12,r.MaxX-.12,r.MaxY-.12),new Color(.27f,.33f,.38f),-799);
                }
                else Wall(r);
            }
            foreach(var room in LunarBasePlan.Rooms)
            {
                var area=room.Area;double x=(area.MinX+area.MaxX)/2,y=area.MaxY-1.15;
                Solid("Room heading",new BaseRect(x-5.5,y-.5,x+5.5,y+.5),new Color(.09f,.13f,.17f),-760);
                Heading(room.Name,new Vector3((float)x,(float)y,0),.1f);
            }
            Heading("← LOGÍSTICA / AGUA       |       LABORATORIO / EVA →",new Vector3(34,22,0),.07f);
            foreach(var station in scenario.Layout.Stations)
            {
                if(station.Position.X>=LunarBasePlan.InnerHatch.MinX)continue;
                double x=station.Position.X,y=station.Position.Y;
                double width=station.Id.Value.StartsWith("suit-")||station.Id.Value=="consumables-panel"?1:1.4;
                // Flush equipment bays and dark caption plates; all remain purely visual.
                Solid("Equipment bay",new BaseRect(x-width,y-.9,x+width,y+1.2),new Color(.11f,.14f,.17f),-740);
                Solid("Bay top trim",new BaseRect(x-width,y+1.15,x+width,y+1.2),new Color(.61f,.77f,.79f),-739);
                Solid("Reading plate",new BaseRect(x-width-.04,y-1.4,x+width+.04,y-.84),new Color(.055f,.065f,.08f),-738);
            }
            var chamber=LunarBasePlan.Chamber;
            Solid("Airlock information plate",new BaseRect(chamber.MinX+.2,chamber.MaxY-1.5,chamber.MaxX-.2,chamber.MaxY-.2),new Color(.055f,.065f,.08f),-738);
            Solid("Shelter information plate",new BaseRect(53.5,11.65,58.6,12.8),new Color(.055f,.065f,.08f),-738);
        }
        private void Heading(string caption,Vector3 position,float size)
        {
            var go=new GameObject(caption);go.transform.SetParent(transform,false);go.transform.localPosition=position;
            var text=go.AddComponent<TextMesh>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize=64;text.characterSize=size;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
            text.text=caption;text.color=new Color(.7f,.81f,.86f);
            var mesh=go.GetComponent<MeshRenderer>();mesh.sharedMaterial=text.font.material;mesh.sortingOrder=2500;
        }
        private void TileFloor(string name,BaseRect area)
        {
            var r=Renderer(name,area,grid,-1000,Color.white);r.drawMode=SpriteDrawMode.Tiled;r.size=new Vector2((float)area.Width,(float)area.Height);
        }
        private void Wall(BaseRect r)
        {
            Solid("Wall graphite outline",r,new Color(.075f,.09f,.105f),-800);
            double inset=System.Math.Min(.055,System.Math.Min(r.Width,r.Height)/4);
            Solid("Wall ivory face",new BaseRect(r.MinX+inset,r.MinY+inset,r.MaxX-inset,r.MaxY-inset),new Color(.76f,.79f,.8f),-799);
            double line=System.Math.Min(.045,System.Math.Min(r.Width,r.Height)/5);
            Solid("Wall light edge",new BaseRect(r.MinX+inset,r.MaxY-inset-line,r.MaxX-inset,r.MaxY-inset),Color.white,-798);
        }
        private void Solid(string name,BaseRect area,Color color,int order)
        {
            var r=Renderer(name,area,white,order,color);
            r.transform.localScale=new Vector3((float)area.Width,(float)area.Height,1);
        }
        private SpriteRenderer Renderer(string name,BaseRect area,Sprite sprite,int order,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            go.transform.localPosition=new Vector3((float)((area.MinX+area.MaxX)/2),(float)((area.MinY+area.MaxY)/2),0);
            var r=go.AddComponent<SpriteRenderer>();r.sprite=sprite;r.sharedMaterial=surfaceMaterial;r.sortingOrder=order;r.color=color;return r;
        }
        private void OnDestroy()
        {
            Release(grid);Release(white);Release(gridTexture);
        }
        private static void Release(Object value)
        {if(value==null)return;if(UnityEngine.Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
