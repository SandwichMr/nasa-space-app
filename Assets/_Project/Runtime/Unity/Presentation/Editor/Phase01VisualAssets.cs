using System;
using System.IO;
using Trainer.Unity.World;
using UnityEditor;
using UnityEngine;

namespace Trainer.Unity.Presentation.Editor
{
    /// <summary>Explicit, repeatable authoring of D4 assets only. Never opens or saves a scene.</summary>
    public static class Phase01VisualAssets
    {
        public const string RoomPath = "Assets/_Project/Prefabs/World/PowerRoom.prefab";
        public const string HudPath = "Assets/_Project/Prefabs/UI/MissionHUD.prefab";
        private const string Art = "Assets/_Project/Art/";

        [MenuItem("Tools/Trainer/Phase 01/Rebuild D4 visual prefabs")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string folder in new[] { "Sprites", "Icons" })
            foreach (string path in Directory.GetFiles(Art + folder + "/Phase01", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            // Use the installed Universal 2D unlit shader, without adding a scene light or settings.
            string materialPath = Art + "Sprites/Phase01/Phase01Sprite.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) throw new InvalidOperationException("Installed Universal 2D unlit shader unavailable");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else { material.shader = shader; EditorUtility.SetDirty(material); }

            var room = new GameObject("PowerRoom");
            try
            {
                var background = AddSprite(room.transform, "Room shell", "power-room", material, -10);
                background.transform.localPosition = Vector3.zero;
                var view = room.AddComponent<PowerRoomView>();
                var stations = new PowerStationView[3];
                for (int i = 0; i < 3; i++)
                {
                    string[] names = { "BatteryStation", "EssentialStation", "ScienceStation" };
                    string[] sprites = { "battery", "essential-service", "science-instrument" };
                    string[] icons = { "battery-icon", "essential-icon", "science-icon" };
                    var root = new GameObject(names[i]);
                    var station = root.AddComponent<PowerStationView>();
                    var body = AddSprite(root.transform, "Body", sprites[i], material, 0);
                    var selection = AddSprite(root.transform, "Selection", "station-selection", material, 2);
                    selection.enabled = false;
                    SpriteRenderer fill = null;
                    if (i == 0)
                    {
                        fill = AddSprite(root.transform, "Confirmed energy", "battery-charge", material, 1);
                        fill.transform.localPosition = new Vector3(0, .20f, 0);
                        fill.enabled = false;
                    }
                    var serialized = new SerializedObject(station);
                    serialized.FindProperty("kind").enumValueIndex = i;
                    serialized.FindProperty("body").objectReferenceValue = body;
                    serialized.FindProperty("selection").objectReferenceValue = selection;
                    serialized.FindProperty("energyFill").objectReferenceValue = fill;
                    serialized.FindProperty("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        Art + "Icons/Phase01/" + icons[i] + ".png");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    // Separate reusable station prefabs, nested into the composed room.
                    string stationPath = "Assets/_Project/Prefabs/World/" + names[i] + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, stationPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(stationPath));
                    root.transform.SetParent(room.transform, false);
                    root.transform.localPosition = new Vector3((i - 1) * 3.62f, .62f, 0);
                    stations[i] = root.GetComponent<PowerStationView>();
                }
                var roomData = new SerializedObject(view);
                var array = roomData.FindProperty("stations");
                array.arraySize = stations.Length;
                for (int i = 0; i < stations.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = stations[i];
                roomData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(room, RoomPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(room); }

            // Preserve the established prefab and script GUID; serialize only the new width field.
            var hud = PrefabUtility.LoadPrefabContents(HudPath);
            try
            {
                var serialized = new SerializedObject(hud.GetComponent<MissionHUD>());
                serialized.FindProperty("panelWidth").floatValue = 430;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(hud, HudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
            AssetDatabase.SaveAssets();
            Debug.Log("D4_VISUAL_PREFABS_BUILT: room + 3 stations; 11 original PNG sprites/icons");
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, string sprite, Material material, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Sprites/Phase01/" + sprite + ".png");
            if (renderer.sprite == null) throw new InvalidOperationException("Missing sprite: " + sprite);
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            return renderer;
        }
    }
}
