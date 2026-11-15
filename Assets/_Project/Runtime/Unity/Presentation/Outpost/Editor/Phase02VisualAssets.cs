using System;
using System.IO;
using System.Linq;
using Trainer.Application.Development;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Trainer.Unity.Presentation.Outpost.Editor
{
    /// <summary>Explicit authoring of D4's exclusive assets. Never saves or edits a scene.</summary>
    public static class Phase02VisualAssets
    {
        public const string Art = "Assets/_Project/Art/Sprites/Phase02/";
        public const string Animation = "Assets/_Project/Art/Animations/Phase02/";
        public const string CrewPath = "Assets/_Project/Prefabs/Crew/Phase02/AstronautVisual.prefab";
        public const string StationPath = "Assets/_Project/Prefabs/World/Phase02/StationVisual.prefab";
        public const string BasePath = "Assets/_Project/Prefabs/World/Phase02/BaseInterior.prefab";
        public const string PanelPath = "Assets/_Project/Prefabs/UI/Phase02/OutpostPanel.prefab";
        private const string PhaseOne = "Assets/_Project/Art/Sprites/Phase01/";
        private const string AdapterPath = "Assets/_Project/Runtime/Unity/World/Outpost/D3CrewVisualAdapter.cs";

        [MenuItem("Tools/Trainer/Phase 02/Rebuild D4 visual assets (no scenes)")]
        public static void Build()
        {
            EnsureFolder(Animation);
            foreach (string path in new[] { CrewPath, StationPath, BasePath, PanelPath }) EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in Directory.GetFiles(Art, "*.png")) ImportSprite(path);
            ImportSheet();
            var material = AssetDatabase.LoadAssetAtPath<Material>(PhaseOne + "Phase01Sprite.mat");
            if (material == null) throw new InvalidOperationException("Missing existing Phase01 URP sprite material");
            BuildCrew(material);
            BuildStation(material);
            BuildInterior(material);
            var go = new GameObject("OutpostPanel (bind the real participant port)");
            try
            {
                var panel = go.AddComponent<OutpostPanel>();
                Set(panel, "energyIcon", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Icons/Phase01/battery-icon.png"));
                Set(panel, "objectiveIcon", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Icons/Phase01/science-icon.png"));
                PrefabUtility.SaveAsPrefabAsset(go, PanelPath);
            }
            finally { Object.DestroyImmediate(go); }
            AssetDatabase.SaveAssets();
            Debug.Log("D4_PHASE02_ASSETS_BUILT: 16 sliced astronaut frames, 8 looping clips, controller, 4 real prefabs; Phase01 unchanged");
        }

        private static void ImportSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = path.EndsWith("floor-tile.png", StringComparison.Ordinal) ? 128 : 100;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        private static void ImportSheet()
        {
            string path = Art + "astronaut-sheet.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int w = texture.width/4, h = texture.height/4;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.spriteImportMode = SpriteImportMode.Multiple;
            // A cell is 1.28 m high. The root is the ground-disc centre, not the helmet centre.
            importer.spritePixelsPerUnit = h/1.28f;
            importer.maxTextureSize = 2048;
            var sprites = new SpriteMetaData[16];
            for (int row = 0; row < 4; row++)
            for (int frame = 0; frame < 4; frame++)
                sprites[row*4+frame] = new SpriteMetaData {
                    name = "crew-" + row + "-" + frame,
                    rect = new Rect(frame*w,(3-row)*h,w,h),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(.5f,.18f)
                };
#pragma warning disable CS0618 // Supported Unity importer API; keeps deterministic slice names without packages.
            importer.spritesheet = sprites;
#pragma warning restore CS0618
            importer.SaveAndReimport();
        }

        private static void BuildCrew(Material material)
        {
            string controllerPath = Animation + "Astronaut.controller";
            // Update existing clips/controller rather than replacing their GUIDs on a repeat authoring run.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.states) layer.stateMachine.RemoveState(child.state);
                foreach (var transition in layer.stateMachine.anyStateTransitions) layer.stateMachine.RemoveAnyStateTransition(transition);
            }
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.AddParameter("Facing", AnimatorControllerParameterType.Int);
            controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(Art+"astronaut-sheet.png").OfType<Sprite>().ToArray();
            for (int direction = 0; direction < 4; direction++)
            for (int walking = 0; walking < 2; walking++)
            {
                string name = (walking == 0 ? "Idle" : "Walk") + new[] { "Down", "Right", "Up", "Left" }[direction];
                string clipPath = Animation + name + ".anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, clipPath); }
                clip.name = name; clip.frameRate = walking == 0 ? 2 : 8;
                int[] frames = walking == 0 ? new[] { 0, 1, 0 } : new[] { 2, 0, 3, 0, 2 };
                float dt = walking == 0 ? .5f : .125f;
                var keys = new ObjectReferenceKeyframe[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    string spriteName = "crew-" + direction + "-" + frames[i];
                    keys[i] = new ObjectReferenceKeyframe { time = i*dt, value = sprites.Single(s => s.name == spriteName) };
                }
                AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("Body",typeof(SpriteRenderer),"m_Sprite"),keys);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip,settings);
                var state = controller.layers[0].stateMachine.AddState(name);
                state.motion = clip;
                var transition = controller.layers[0].stateMachine.AddAnyStateTransition(state);
                transition.hasExitTime = false; transition.duration = 0; transition.canTransitionToSelf = false;
                transition.AddCondition(AnimatorConditionMode.Equals,direction,"Facing");
                transition.AddCondition(walking == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,0,"Walking");
                EditorUtility.SetDirty(clip);
            }
            var root = new GameObject("AstronautVisual (root owned by D3)");
            try
            {
                var visual = root.AddComponent<CrewVisual>();
                var marker = Sprite(root.transform,"Crew footprint",Load("crew-marker"),material,0);
                marker.transform.localScale = new Vector3(.30f,.30f,1);
                var body = Sprite(root.transform,"Body",sprites.Single(s=>s.name=="crew-0-0"),material,1);
                var badge = Text(root.transform,"Number and local identity",new Vector2(0,-.40f),.075f,42,Color.white);
                var animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                Set(visual,"body",body); Set(visual,"marker",marker); Set(visual,"badge",badge); Set(visual,"animator",animator);
                Type adapter = typeof(CrewVisual).Assembly.GetType("Trainer.Unity.World.Outpost.D3CrewVisualAdapter");
                if (adapter != null) root.AddComponent(adapter);
                PrefabUtility.SaveAsPrefabAsset(root,CrewPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildStation(Material material)
        {
            var root = new GameObject("StationVisual (walkable floor terminal)");
            try
            {
                var view = root.AddComponent<OutpostStationView>();
                var body = Sprite(root.transform,"Physical floor terminal",Load("water-station"),material,0);
                body.transform.localScale = Vector3.one * .5f;
                var selection = Sprite(root.transform,"Confirmed proximity",AssetDatabase.LoadAssetAtPath<Sprite>(PhaseOne+"station-selection.png"),material,-2);
                selection.transform.localScale = Vector3.one * .56f;
                var progress = Sprite(root.transform,"Confirmed task progress",Load("white-pixel"),material,2500);
                progress.transform.localScale = new Vector3(1,.07f,1);
                // White sprite is .08 m; scale the transform in metres with a unit-size renderer.
                progress.drawMode = SpriteDrawMode.Sliced; progress.size = Vector2.one;
                var title = Text(root.transform,"Station name",new Vector2(0,1.12f),.09f,44,Color.white);
                var status = Text(root.transform,"Occupant availability and measured progress",new Vector2(.88f,0),.055f,40,new Color(.83f,.91f,1));
                status.anchor = TextAnchor.MiddleLeft; status.alignment = TextAlignment.Left;
                Set(view,"body",body); Set(view,"selection",selection); Set(view,"progress",progress); Set(view,"title",title); Set(view,"status",status);
                view.Clear();
                PrefabUtility.SaveAsPrefabAsset(root,StationPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildInterior(Material material)
        {
            var root = new GameObject("BaseInterior (inject D2 scenario; serialized preview is synthetic)");
            try
            {
                var view = root.AddComponent<BaseInteriorView>();
                Set(view,"floor",Load("floor-tile")); Set(view,"wall",Load("wall-panel")); Set(view,"stripe",Load("white-pixel"));
                Set(view,"waterStation",Load("water-station")); Set(view,"airStation",Load("air-station"));
                Set(view,"scienceStation",AssetDatabase.LoadAssetAtPath<Sprite>(PhaseOne+"science-instrument.png"));
                Set(view,"genericStation",AssetDatabase.LoadAssetAtPath<Sprite>(PhaseOne+"battery.png"));
                Set(view,"material",material);
                Set(view,"stationPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(StationPath).GetComponent<OutpostStationView>());
                // Baked preview uses ONLY the frozen common fixture. Production Bind always replaces it.
                view.Configure(OutpostFixtures.Scenario());
                PrefabUtility.SaveAsPrefabAsset(root,BasePath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Trainer/Phase 02/Install D3 visual adapter after integration")]
        public static void InstallD3Adapter()
        {
            if (typeof(CrewVisual).Assembly.GetType("Trainer.Unity.Networking.Outpost.ICrewAvatarView") == null)
                throw new InvalidOperationException("D3 branch is not present. Keep the common base intact; run after D2 integration.");
            const string template = "Assets/_Project/Runtime/Unity/Presentation/Outpost/Editor/D3CrewVisualAdapter.cs.txt";
            if (File.Exists(AdapterPath) && File.ReadAllText(AdapterPath) != File.ReadAllText(template))
                throw new InvalidOperationException("Existing adapter differs; preserve manual edits.");
            File.Copy(template,AdapterPath,true);
            AssetDatabase.Refresh();
            Debug.Log("D4_D3_ADAPTER_INSTALLED: after compilation run Attach D3 adapter to astronaut prefab.");
        }

        [MenuItem("Tools/Trainer/Phase 02/Attach D3 adapter to astronaut prefab")]
        public static void AttachD3Adapter()
        {
            Type adapter = typeof(CrewVisual).Assembly.GetType("Trainer.Unity.World.Outpost.D3CrewVisualAdapter");
            if (adapter == null) throw new InvalidOperationException("Install D3 adapter and wait for compilation first.");
            var root = PrefabUtility.LoadPrefabContents(CrewPath);
            try
            {
                if (root.GetComponent(adapter) == null) root.AddComponent(adapter);
                PrefabUtility.SaveAsPrefabAsset(root,CrewPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("D4_D3_ADAPTER_ATTACHED: astronaut prefab preserves its existing art and animation references.");
        }

        private static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art+name+".png");
        private static SpriteRenderer Sprite(Transform parent,string name,Sprite sprite,Material material,int order)
        {
            if (sprite == null) throw new InvalidOperationException("Missing sprite: "+name);
            var root = new GameObject(name); root.transform.SetParent(parent,false);
            var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.sortingOrder = order;
            return renderer;
        }
        private static TextMesh Text(Transform parent,string name,Vector2 position,float size,int fontSize,Color color)
        {
            var root = new GameObject(name); root.transform.SetParent(parent,false); root.transform.localPosition = position;
            var text = root.AddComponent<TextMesh>(); text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.characterSize = size; text.fontSize = fontSize; text.color = color;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = text.font.material; renderer.sortingOrder = 3000;
            return text;
        }
        internal static void Set(Object target,string field,Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void EnsureFolder(string path)
        {
            path = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path),Path.GetFileName(path));
        }
    }
}
