using System;
using System.IO;
using Trainer.Unity.World.Expedition;
using UnityEditor;
using UnityEngine;
namespace Trainer.Unity.Presentation.Expedition.Editor
{
    public static class Phase03VisualAssets
    {
        private const string Root="Assets/_Project/";
        private static string Art=>Root+"Art/Phase03/Sprites/";
        [MenuItem("Tools/Trainer/Phase 03/Build D4 visual prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(Root+"Prefabs/World/Phase03");Directory.CreateDirectory(Root+"Prefabs/UI/Phase03");Directory.CreateDirectory(Root+"Prefabs/Crew/Phase03");
            AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles(Art,"*.png"))
            {
                var t=(TextureImporter)AssetImporter.GetAtPath(path);t.textureType=TextureImporterType.Sprite;t.spriteImportMode=SpriteImportMode.Single;
                t.spritePixelsPerUnit=128;t.spritePivot=Vector2.one*.5f;t.alphaIsTransparency=true;t.mipmapEnabled=false;
                t.textureCompression=TextureImporterCompression.Uncompressed;t.filterMode=FilterMode.Bilinear;
                var settings=new TextureImporterSettings();t.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;t.SetTextureSettings(settings);t.SaveAndReimport();
            }
            var science=Station("ScienceVisual","science");var optional=Station("OptionalVisual","optional");
            Sign("ShelterVisual","shelter");Sign("AirlockVisual","hatch-closed");
            var world=new GameObject("ExpeditionWorld");var view=world.AddComponent<ExpeditionWorldView>();
            foreach(string field in new[]{"regolith","rock","route","shelter","antenna"}) Set(view,field,Sprite(field));
            Set(view,"hatchOpen",Sprite("hatch-open"));Set(view,"hatchClosed",Sprite("hatch-closed"));Set(view,"hatchCycle",Sprite("hatch-cycle"));
            Set(view,"material",Material);Set(view,"sciencePrefab",science.GetComponent<Trainer.Unity.World.Outpost.OutpostStationView>());Set(view,"optionalPrefab",optional.GetComponent<Trainer.Unity.World.Outpost.OutpostStationView>());
            Save(world,"World","ExpeditionWorld");
            var panel=new GameObject("ExpeditionPanel");var hud=panel.AddComponent<ExpeditionPanel>();Set(hud,"warning",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"warning.png"));Save(panel,"UI","ExpeditionPanel");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/Crew/Phase02/AstronautVisual.prefab");
            var crew=(GameObject)PrefabUtility.InstantiatePrefab(original);crew.name="ExpeditionAstronaut";
            var badge=crew.AddComponent<ExpeditionCrewBadge>();var label=Label(crew.transform,"Expedition state",new Vector3(0,-1,0),.20f);Set(badge,"label",label);
            Save(crew,"Crew","ExpeditionAstronaut");AssetDatabase.SaveAssets();
            Debug.Log("D4_BUILD_OK: 11 sprites, 7 prefabs; original astronaut retained as prefab variant.");
        }
        private static Material Material=>AssetDatabase.LoadAssetAtPath<Material>(Root+"Art/Sprites/Phase01/Phase01Sprite.mat");
        private static Sprite Sprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(Art+name+".png");
        private static GameObject Station(string name,string sprite)
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/World/Phase02/StationVisual.prefab");
            var g=(GameObject)PrefabUtility.InstantiatePrefab(original);g.name=name;
            var station=g.GetComponent<Trainer.Unity.World.Outpost.OutpostStationView>();
            var so=new SerializedObject(station);var body=(SpriteRenderer)so.FindProperty("body").objectReferenceValue;body.sprite=Sprite(sprite);body.transform.localScale=Vector3.one;
            var title=(TextMesh)so.FindProperty("title").objectReferenceValue;title.characterSize=.038f;title.transform.localPosition=new Vector3(0,.85f,0);
            var status=(TextMesh)so.FindProperty("status").objectReferenceValue;status.characterSize=.035f;status.transform.localPosition=new Vector3(.7f,0,0);
            return Save(g,"World",name);
        }
        private static void Sign(string name,string sprite)
        {
            var g=new GameObject(name);var r=g.AddComponent<SpriteRenderer>();r.sprite=Sprite(sprite);r.sharedMaterial=Material;r.sortingOrder=1500;Save(g,"World",name);
        }
        internal static TextMesh Label(Transform parent,string name,Vector3 position,float size)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=position;
            var t=g.AddComponent<TextMesh>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=48;t.characterSize=size*.25f;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;
            var r=g.GetComponent<MeshRenderer>();r.sharedMaterial=t.font.material;r.sortingOrder=3000;return t;
        }
        private static GameObject Save(GameObject go,string kind,string name)
        {
            var saved=PrefabUtility.SaveAsPrefabAsset(go,Root+"Prefabs/"+kind+"/Phase03/"+name+".prefab");UnityEngine.Object.DestroyImmediate(go);return saved;
        }
        internal static void Set(UnityEngine.Object obj,string name,UnityEngine.Object value)
        {
            var so=new SerializedObject(obj);var p=so.FindProperty(name);if(p==null)throw new Exception("Missing field "+name);p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
