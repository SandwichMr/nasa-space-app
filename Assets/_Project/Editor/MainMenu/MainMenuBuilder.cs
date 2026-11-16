using System;
using System.IO;
using System.Linq;
using Trainer.Unity.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEditor.Build.Reporting;

namespace Trainer.Editor.MainMenu
{
    public static class MainMenuBuilder
    {
        private const string Root = "Assets/_Project/";
        private static Font font;
        private static Sprite panel, button, primary;
        private static readonly Color White = new Color(.88f, .94f, 1);
        private static readonly Color Cyan = new Color(.43f, .88f, .94f);
        [MenuItem("Tools/Trainer/Main Menu/Create scene and prefab")]
        public static void Create()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel = Sliced("Art/Icons/LunarHud/frame-panel.png",12,"Panel");
            button = Sliced("Art/Icons/LunarHud/button.png",6,"Button");
            primary = Sliced("Art/Icons/LunarHud/button-active.png",6,"Primary");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var go = new GameObject("Main Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MainMenuView));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; scaler.matchWidthOrHeight = .5f;
            var v = go.GetComponent<MainMenuView>();
            const string configPath = Root + "Data/MainMenu/PublicEndpoint.asset";
            v.configuration = AssetDatabase.LoadAssetAtPath<MainMenuConfiguration>(configPath);
            if (v.configuration == null) { v.configuration = ScriptableObject.CreateInstance<MainMenuConfiguration>(); AssetDatabase.CreateAsset(v.configuration, configPath); }
            var bg = Image(go.transform, "Lunar surface", Load("Art/Sprites/Environment/LunarBase/LunarRegolith.png"), 0, 0, 1280, 800, new Color(.12f,.18f,.27f));
            bg.raycastTarget = false; Stretch(bg.rectTransform);
            var veil = Image(go.transform, "Night", null, 0,0,1280,800,new Color(.015f,.035f,.07f,.86f)); Stretch(veil.rectTransform);
            // Fixed safe layout fits 4:3 at the canvas scaler's smallest supported size.
            Text(go.transform,"Brand", "SSB   /   MISIÓN COOPERATIVA", 65,45,800,30,16,Cyan);
            Text(go.transform,"Title","SPACE STATION\nBREAKDOWN",65,98,650,130,52,White,FontStyle.Bold);
            Text(go.transform,"Tagline","Mantén la base. Coopera. Explora.",68,244,680,35,22,White);
            v.version = Text(go.transform,"Version","",65,754,900,24,13,new Color(.57f,.66f,.77f));
            Text(go.transform,"Footer","RECURSOS COMPARTIDOS · DECISIONES EN EQUIPO",65,718,900,25,13,Cyan);
            Hero(go.transform);
            v.launchAdapter = go.AddComponent<MainMenuNetworkAdapter>();
            v.home = Page(go.transform,"Home",65,315,440,370);
            v.play = Button(v.home.transform,"Jugar online",0,0,440,66,true);
            v.helpButton = Button(v.home.transform,"Cómo jugar",0,82,440,52);
            v.settingsButton = Button(v.home.transform,"Ajustes",0,146,440,52);
            v.creditsButton = Button(v.home.transform,"Créditos",0,210,440,52);
            v.quit = Button(v.home.transform,"Salir",0,274,440,52);
            v.help = Modal(go.transform,"Help","CÓMO JUGAR");
            Text(v.help.transform,"Instructions","WASD / FLECHAS  ·  Moverte por la base\nE  ·  Interactuar con la estación cercana\nR  ·  Solicitar regreso cuando esté disponible\n\nLos recursos son de todo el equipo.\nCoordina el mantenimiento y las tareas científicas.\nAntes de explorar, deja margen para regresar.\n\nTAB y ENTER navegan el menú. ESC vuelve.",44,105,800,365,23,White);
            var closeHelp = Button(v.help.transform,"Volver",44,500,260,54);
            v.credits = Modal(go.transform,"Credits","CRÉDITOS");
            Text(v.credits.transform,"Attribution","SPACE STATION BREAKDOWN\nProyecto del equipo · NASA Space Apps 2026\n\nCreado con Unity Universal 2D y C#.\nArte reutilizado del proyecto: base lunar, estaciones,\ntripulación y HUD. Astronauta generado con OpenAI\nimage_gen; procedencia detallada en docs/main-menu.\n\nSin logotipos ni aval de NASA.\nEste menú no incorpora datos científicos nuevos.",44,100,800,375,22,White);
            var closeCredits = Button(v.credits.transform,"Volver",44,500,260,54);
            v.closeButtons = new[] { closeHelp, closeCredits };
            v.multiplayer = Modal(go.transform,"Multiplayer","MULTIPLAYER");
            Text(v.multiplayer.transform,"Intro","ÚNETE A LA BASE\n\nCoopera con tu tripulación en el servidor compartido.\nLa dirección ya está configurada: solo conecta.",44,105,800,170,24,White);
            v.endpointLabel = Text(v.multiplayer.transform,"Public endpoint","",44,310,800,85,20,Cyan);
            Text(v.multiplayer.transform,"Integration note","El servidor debe estar disponible para conectar.",44,420,790,48,18,White);
            v.joinOnline = Button(v.multiplayer.transform,"Conectarse",44,500,350,54,true);
            v.multiplayerBack = Button(v.multiplayer.transform,"Volver",430,500,370,54);
            v.connection = Modal(go.transform,"Connection","JUGAR ONLINE");
            v.connectionDetail = Text(v.connection.transform,"Connection status","",44,135,800,215,28,White);
            Text(v.connection.transform,"Honest status","La partida se confirma al recibir la admisión\ny el estado inicial del servidor.",44,350,780,85,20,Cyan);
            v.retry = Button(v.connection.transform,"Reintentar",44,500,330,54,true);
            v.back = Button(v.connection.transform,"Volver / Cancelar",410,500,390,54);
            v.settings = Modal(go.transform,"Settings","AJUSTES");
            v.volumeLabel = Text(v.settings.transform,"Volume label","Volumen maestro",44,95,750,32,22,White);
            v.volume = Slider(v.settings.transform,44,145,570,32);
            v.mute = Toggle(v.settings.transform,"Silenciar",650,140,160);
            v.displayLabel = Text(v.settings.transform,"Display label","Pantalla",44,218,780,32,18,Cyan);
            v.resolution = Dropdown(v.settings.transform,44,268,440,48);
            v.fullscreen = Toggle(v.settings.transform,"Pantalla completa",520,272,280);
            v.confirm = Button(v.settings.transform,"Confirmar pantalla",44,360,756,58,true);
            v.confirmLabel = v.confirm.GetComponentInChildren<Text>();
            v.apply = Button(v.settings.transform,"Aplicar",44,500,350,54,true);
            v.cancel = Button(v.settings.transform,"Cancelar",430,500,370,54);
            foreach (var p in new[] {v.help,v.credits,v.connection,v.settings,v.multiplayer}) p.transform.parent.gameObject.SetActive(false);
            v.confirm.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go,Root+"Prefabs/UI/MainMenu/MainMenu.prefab");
            var events = new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            EditorSceneManager.SaveScene(scene,Root+"Scenes/MainMenu.unity");
            AssetDatabase.SaveAssets();
            Debug.Log("[MainMenu] Scene, prefab and public configuration created.");
        }
        private static void Hero(Transform parent)
        {
            var hero = Page(parent,"Mission vignette",565,315,640,370);
            Image(hero.transform,"Frame",panel,0,0,640,370,White);
            Text(hero.transform,"Label","01 / BASE LUNAR",28,20,580,25,17,Cyan);
            var map = Image(hero.transform,"Existing lunar base",Load("Art/Sprites/Environment/LunarBase/LunarBaseInterior.png"),28,60,435,240,White);map.preserveAspect=true;
            var astronaut = AssetDatabase.LoadAllAssetsAtPath(Root+"Art/Sprites/Phase02/astronaut-sheet.png").OfType<Sprite>().FirstOrDefault();
            var crew = Image(hero.transform,"Existing crew",astronaut,480,80,115,150,White);crew.preserveAspect=true;
            foreach (var spec in new[]{new[]{"icon-water","AGUA"},new[]{"icon-oxygen","AIRE"},new[]{"icon-battery","ENERGÍA"}}.Select((a,i)=> new { a,i }))
            {
                Image(hero.transform,spec.a[0],Load("Art/Icons/LunarHud/"+spec.a[0]+".png"),30+spec.i*195,305,30,30,White);
                Text(hero.transform,spec.a[1],spec.a[1],70+spec.i*195,308,140,25,14,White);
            }
        }
        private static Sprite Sliced(string source,float border,string name)
        {
            if(!AssetDatabase.IsValidFolder(Root+"Art/MainMenu")) AssetDatabase.CreateFolder(Root+"Art","MainMenu");
            string path=Root+"Art/MainMenu/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing!=null)return existing;
            var original=Load(source);
            var sprite=Sprite.Create(original.texture,original.rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(border,border,border,border));
            sprite.name=name;AssetDatabase.CreateAsset(sprite,path);return sprite;
        }
        private static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Root+path);
        private static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var go = new GameObject(name,typeof(RectTransform));var rt=go.GetComponent<RectTransform>();rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);return rt;
        }
        private static void Stretch(RectTransform rt) { rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero; }
        private static GameObject Page(Transform parent,string name,float x,float y,float w,float h) => Rect(parent,name,x,y,w,h).gameObject;
        private static GameObject Modal(Transform parent,string name,string title)
        {
            var shade=Image(parent,name,null,0,0,1280,800,new Color(.01f,.025f,.05f,.96f));Stretch(shade.rectTransform);
            var box=Page(shade.transform,"Content",200,100,880,600);
            Image(box.transform,"Panel background",null,0,0,880,600,new Color(.025f,.045f,.09f,1));
            Image(box.transform,"Panel",panel,0,0,880,600,White);
            Text(box.transform,"Heading",title,44,35,800,55,34,Cyan,FontStyle.Bold);
            // The root fills the canvas; return the inner panel through a dedicated layout child.
            return box;
        }
        private static Image Image(Transform p,string name,Sprite sprite,float x,float y,float w,float h,Color color)
        { var im=Rect(p,name,x,y,w,h).gameObject.AddComponent<Image>();im.sprite=sprite;im.type=sprite!=null?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple;im.color=color;return im; }
        private static Text Text(Transform p,string name,string text,float x,float y,float w,float h,int size,Color color,FontStyle style=FontStyle.Normal)
        { var t=Rect(p,name,x,y,w,h).gameObject.AddComponent<Text>();t.font=font;t.text=text;t.fontSize=size;t.color=color;t.fontStyle=style;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t; }
        private static Button Button(Transform p,string label,float x,float y,float w,float h,bool main=false)
        {
            var im=Image(p,label,main?primary:button,x,y,w,h,White);var b=im.gameObject.AddComponent<Button>();
            var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.65f,1,1);colors.selectedColor=new Color(.45f,.95f,1);colors.pressedColor=new Color(.4f,.65f,.85f);b.colors=colors;
            var t=Text(im.transform,"Label",label,12,0,w-24,h,main?25:22,White,main?FontStyle.Bold:FontStyle.Normal);t.alignment=TextAnchor.MiddleCenter;return b;
        }
        private static Slider Slider(Transform p,float x,float y,float w,float h)
        {
            var bg=Image(p,"Volume slider",button,x,y,w,h,White);var s=bg.gameObject.AddComponent<Slider>();
            var area=Rect(bg.transform,"Area",12,0,w-24,h);var fill=Image(area,"Fill",null,0,4,w-24,h-8,Cyan);Stretch(fill.rectTransform);
            var handle=Image(area,"Handle",null,0,0,18,h,White);
            handle.rectTransform.anchorMin=new Vector2(0,0);handle.rectTransform.anchorMax=new Vector2(0,1);handle.rectTransform.sizeDelta=new Vector2(18,0);
            s.fillRect=fill.rectTransform;s.handleRect=handle.rectTransform;s.targetGraphic=handle;s.minValue=0;s.maxValue=1;s.value=.7f;return s;
        }
        private static Toggle Toggle(Transform p,string label,float x,float y,float w)
        {
            var rt=Rect(p,label,x,y,w,40);var t=rt.gameObject.AddComponent<Toggle>();
            var bg=Image(rt,"Box",button,0,4,30,30,White);var mark=Image(bg.transform,"Mark",null,8,8,14,14,Cyan);
            t.targetGraphic=bg;t.graphic=mark;Text(rt,"Label",label,40,4,w-40,36,18,White);return t;
        }
        private static Dropdown Dropdown(Transform p,float x,float y,float w,float h)
        {
            var im=Image(p,"Resolution",button,x,y,w,h,White);var d=im.gameObject.AddComponent<Dropdown>();
            d.captionText=Text(im.transform,"Caption","Resolución",18,8,w-40,h-8,20,White);
            var template=Rect(im.transform,"Template",0,h,w,240);var img=template.gameObject.AddComponent<Image>();img.color=new Color(.04f,.08f,.14f);
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;
            var viewport=Rect(template,"Viewport",0,0,w,240);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect(viewport,"Content",0,0,w,42);var item=Rect(content,"Item",0,0,w,42);
            var toggle=item.gameObject.AddComponent<Toggle>();var background=Image(item,"Background",null,0,0,w,42,new Color(.07f,.16f,.24f));
            var selected=Image(item,"Selected",null,2,2,6,38,Cyan);toggle.targetGraphic=background;toggle.graphic=selected;
            d.itemText=Text(item,"Item label","Resolution",18,0,w-30,42,20,White);d.itemText.alignment=TextAnchor.MiddleLeft;
            scroll.viewport=viewport;scroll.content=content;d.template=template;template.gameObject.SetActive(false);return d;
        }
        public static void PrepareIntegrated()
        {
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(Root+"Scenes/Phase05_LivingBase.unity")==null)
                Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.Build(false);
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(Root+"Scenes/MainMenu.unity",true),new EditorBuildSettingsScene(Root+"Scenes/Phase05_LivingBase.unity",true)};
        }
        public static void BuildLinuxServer()
        {
            Directory.CreateDirectory("Builds/build_linux_Server");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{Root+"Scenes/Phase05_LivingBase.unity"},locationPathName="Builds/build_linux_Server/SpaceStationBreakdownServer.x86_64",target=BuildTarget.StandaloneLinux64,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Server build failed.");
        }
        public static void BuildAll()
        {
            PrepareIntegrated();
            BuildLinux();
            BuildLinuxServer();
        }
        public static void BuildLinux()
        {
            Directory.CreateDirectory("Builds/MainMenu");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{Root+"Scenes/MainMenu.unity",Root+"Scenes/Phase05_LivingBase.unity"},locationPathName="Builds/MainMenu/SpaceStationBreakdown.x86_64",target=BuildTarget.StandaloneLinux64,options=BuildOptions.Development });
            if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Menu build failed: "+report.summary.result);
        }
    }
}
