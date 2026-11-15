using System;
using System.IO;
using Trainer.Unity.World.BaseOperations;
using UnityEditor;
using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations.Editor
{
    /// <summary>Opt-in authoring: writes only D4 Phase04 assets; never saves a scene or changes existing art.</summary>
    public static class Phase04VisualAssets
    {
        private const string Root = "Assets/_Project/";
        private const string Art = Root + "Art/Phase04/Sprites/";
        [MenuItem("Tools/Trainer/Phase 04/Build D4 visual prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "Prefabs/UI/Phase04");
            Directory.CreateDirectory(Root + "Prefabs/World/Phase04");
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = path.Contains("processor") || path.Contains("air-unit") || path.Contains("logistics") ? 256 : 64;
                importer.spritePivot = Vector2.one * .5f;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
            var water = Station("WaterProcessor", "water-processor");
            var air = Station("AirUnit", "air-unit");
            var logistics = Station("LogisticsConsole", "logistics");
            var world = new GameObject("BaseOperationsWorld");
            var view = world.AddComponent<BaseOperationsWorldView>();
            Set(view, "waterPrefab", water.GetComponent<BaseEquipmentView>());
            Set(view, "airPrefab", air.GetComponent<BaseEquipmentView>());
            Set(view, "logisticsPrefab", logistics.GetComponent<BaseEquipmentView>());
            Save(world, "World", "BaseOperationsWorld");
            var panel = new GameObject("BaseOperationsPanel");
            var hud = panel.AddComponent<BaseOperationsPanel>();
            foreach (string field in new[] { "battery", "water", "food", "oxygen", "carbonDioxide" })
                Set(hud, field, AssetDatabase.LoadAssetAtPath<Texture2D>(Art + (field == "carbonDioxide" ? "carbon-dioxide" : field) + ".png"));
            Save(panel, "UI", "BaseOperationsPanel");
            AssetDatabase.SaveAssets();
            Debug.Log("D4_PHASE04_ASSETS_OK: 8 imported sprites, 5 serialized prefabs. Scenes and original astronaut untouched.");
        }

        private static GameObject Station(string name, string sprite)
        {
            var root = new GameObject(name);
            var view = root.AddComponent<BaseEquipmentView>();
            Set(view, "body", SpriteChild(root, "Equipment", AssetDatabase.LoadAssetAtPath<Sprite>(Art + sprite + ".png"), Vector3.zero, Vector3.one * 1.5f, 900));
            var padSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/Sprites/Phase02/crew-marker.png");
            Set(view, "pad", SpriteChild(root, "Floor service marker (nonblocking)", padSprite,
                new Vector3(0, -.35f, 0), new Vector3(1.8f / padSprite.bounds.size.x, .65f / padSprite.bounds.size.y, 1), -780));
            Set(view, "progress", SpriteChild(root, "Confirmed work", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/Sprites/Phase02/white-pixel.png"), new Vector3(0, -.68f, 0), new Vector3(1.2f, .055f, 1), 2500));
            Set(view, "title", Label(root.transform, "Station caption", new Vector3(0, 1.02f, 0), .042f));
            Set(view, "status", Label(root.transform, "Confirmed status and occupant", new Vector3(0, -1.03f, 0), .040f));
            return Save(root, "World", name);
        }
        private static SpriteRenderer SpriteChild(GameObject root, string name, Sprite sprite, Vector3 at, Vector3 scale, int order)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform, false);
            child.transform.localPosition = at; child.transform.localScale = scale;
            var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/Sprites/Phase01/Phase01Sprite.mat");
            return renderer;
        }
        internal static TextMesh Label(Transform parent, string name, Vector3 at, float size)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); child.transform.localPosition = at;
            var text = child.AddComponent<TextMesh>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48; text.characterSize = size; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.color = new Color(.8f, .94f, .96f);
            var renderer = child.GetComponent<MeshRenderer>(); renderer.sharedMaterial = text.font.material; renderer.sortingOrder = 3000;
            return text;
        }
        internal static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException("Missing serialized field " + field);
            property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static GameObject Save(GameObject root, string kind, string name)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(root, Root + "Prefabs/" + kind + "/Phase04/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root); return asset;
        }
    }
}
