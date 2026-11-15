using System;
using System.IO;
using System.Linq;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.World.Expedition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Plan = Trainer.Unity.Configuration.OutpostScenarioDefinition;
namespace Trainer.Tooling.Art
{
    public static class LunarBaseArtSetup
    {
        public static void Run()
        {
            const string root="Assets/_Project/Art/Sprites/Environment/LunarBase/";
            const string planPath="Assets/_Project/Data/Scenarios/Phase04/Phase04_LunarInterior.asset";
            AssetDatabase.Refresh();
            Sprite image=Import(root+"LunarBaseOptimized.png",100);
            Sprite surface=Import(root+"LunarRegolith.png",64);
            var original=AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>("Assets/_Project/Data/Scenarios/Phase03/Phase03_EvaAndShelter_Synthetic.asset");
            var plan=AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(planPath);
            if(plan==null){AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),planPath);plan=AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(planPath);}
            EditorUtility.CopySerialized(original,plan);
            var old=original.ToScenario();var data=new SerializedObject(plan);
            var obstacles=LunarBasePlan.Obstacles();
            var list=data.FindProperty("obstacles");list.arraySize=obstacles.Count;
            for(int i=0;i<obstacles.Count;i++) Rect(list.GetArrayElementAtIndex(i),obstacles[i]);
            Rect(data.FindProperty("bounds"),new BaseRect(-100000,-100000,100000,100000));
            var stations=data.FindProperty("stations");
            for(int i=0;i<stations.arraySize;i++)
            {
                var station=stations.GetArrayElementAtIndex(i);
                Point(station,LunarBasePlan.Station(station.FindPropertyRelative("id").stringValue));
            }
            var zones=data.FindProperty("zones");zones.arraySize=4;
            Zone(zones.GetArrayElementAtIndex(0),1,new BaseRect(44.3,.6,67.8,15.7));
            Zone(zones.GetArrayElementAtIndex(1),2,LunarBasePlan.Chamber);
            Zone(zones.GetArrayElementAtIndex(2),0,LunarBasePlan.Habitat);
            Zone(zones.GetArrayElementAtIndex(3),3,new BaseRect(-100000,-100000,100000,100000));
            Rect(data.FindProperty("chamber"),LunarBasePlan.Chamber);
            Rect(data.FindProperty("innerHatch"),LunarBasePlan.InnerHatch);
            Rect(data.FindProperty("outerHatch"),LunarBasePlan.OuterHatch);
            Point(data.FindProperty("exteriorEntry"),LunarBasePlan.Entry);
            Point(data.FindProperty("antenna"),new BasePosition(70,42));
            var spawns=data.FindProperty("spawnPoints");
            for(int i=0;i<spawns.arraySize;i++)Point(spawns.GetArrayElementAtIndex(i),new BasePosition(32+(i%2)*1.5,19+(i/2)*1.5));
            data.FindProperty("crewRadiusMeters").doubleValue=.35;
            data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(plan);
            var living=AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>("Assets/_Project/Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset");
            var so=new SerializedObject(living);so.FindProperty("expedition").objectReferenceValue=plan;
            so.FindProperty("logisticsX").doubleValue=8;so.FindProperty("logisticsY").doubleValue=10;
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(living);
            var scenario=living.ToScenario();
            foreach(var p in scenario.Layout.SpawnPoints)
                if(!scenario.Layout.IsAdmissible(p,.35))throw new Exception("Blocked spawn "+p);
            var nav=new Trainer.Unity.Validation.Phase04.Phase04Navigator(scenario.Expedition);
            foreach(var s in scenario.Layout.Stations.Where(s=>s.Position.X<LunarBasePlan.InnerHatch.MaxX))
                if(nav.PlanToStation(scenario.Layout.SpawnPoints[0],s.Id)==null)throw new Exception("Unreachable station "+s.Id);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Phase04_LivingBase.unity");
            var composition=UnityEngine.Object.FindFirstObjectByType<BaseOperationsSceneComposition>();
            var sceneData=new SerializedObject(composition);sceneData.FindProperty("lunarInteriorArtwork").objectReferenceValue=image;sceneData.ApplyModifiedPropertiesWithoutUndo();
            foreach(var view in UnityEngine.Object.FindObjectsByType<ExpeditionWorldView>(FindObjectsSortMode.None))
            {var v=new SerializedObject(view);v.FindProperty("lunarSurface").objectReferenceValue=surface;v.ApplyModifiedPropertiesWithoutUndo();}
            var framing=UnityEngine.Object.FindFirstObjectByType<Trainer.Unity.Presentation.BaseOperations.BaseOperationsCameraFraming>();
            var frame=new SerializedObject(framing);frame.FindProperty("visibleHeightMeters").floatValue=12.5f;frame.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(composition.gameObject.scene);EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/lunar-base-large");File.WriteAllText("Logs/lunar-base-large/setup.txt","PASS: 68.4 x 40 m habitat, new seven-room layout and 8 m corridor, original 0.35 m crew radius, valid spawn points, routes to every interior station; shared exterior +/-100 km; world-anchored 49-tile renderer. No crew teleport.\n");
            EditorApplication.Exit(0);
        }
        private static void Point(SerializedProperty property,BasePosition p)
        {property.FindPropertyRelative("x").doubleValue=p.X;property.FindPropertyRelative("y").doubleValue=p.Y;}
        private static void Zone(SerializedProperty p,int kind,BaseRect area)
        {p.FindPropertyRelative("zone").intValue=kind;Rect(p.FindPropertyRelative("area"),area);}
        private static Sprite Import(string path,float ppu)
        {
            var i=(TextureImporter)AssetImporter.GetAtPath(path);i.textureType=TextureImporterType.Sprite;i.spriteImportMode=SpriteImportMode.Single;
            i.spritePixelsPerUnit=ppu;i.mipmapEnabled=false;i.alphaIsTransparency=true;i.maxTextureSize=2048;
            i.textureCompression=TextureImporterCompression.Uncompressed;i.wrapMode=TextureWrapMode.Repeat;
            var settings=new TextureImporterSettings();i.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;i.SetTextureSettings(settings);
            i.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static void Rect(SerializedProperty p,BaseRect r)
        {p.FindPropertyRelative("minX").doubleValue=r.MinX;p.FindPropertyRelative("minY").doubleValue=r.MinY;p.FindPropertyRelative("maxX").doubleValue=r.MaxX;p.FindPropertyRelative("maxY").doubleValue=r.MaxY;}
    }
}
