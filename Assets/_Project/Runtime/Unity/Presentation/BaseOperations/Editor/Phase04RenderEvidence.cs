using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations.Editor
{
    /// <summary>Opt-in fixed-resolution Game view for native captures; removes its temporary custom size afterwards.</summary>
    public static class Phase04RenderEvidence
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static EditorWindow view;
        private static object group;
        private static int previousIndex, customIndex = -1;
        private static int fixedIndex;
        public static int Width => Number("-d4-width", 1440);
        public static int Height => Number("-d4-height", 900);
        public static string DirectoryPath(string fallback) => Path.GetFullPath(Value("-d4-evidence") ?? fallback);

        public static void OpenGameView()
        {
            var assembly = typeof(EditorWindow).Assembly;
            Type viewType = assembly.GetType("UnityEditor.GameView");
            view = (EditorWindow)ScriptableObject.CreateInstance(viewType);
            view.ShowUtility(); view.position = new Rect(40, 40, Width, Math.Min(Height + 30, 1150)); view.Focus();
            var index = viewType.GetProperty("selectedSizeIndex", Members);
            previousIndex = (int)index.GetValue(view);
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            MethodInfo getGroup = sizesType.GetMethod("GetGroup", Members);
            group = getGroup.Invoke(sizes, new[] { Enum.Parse(getGroup.GetParameters()[0].ParameterType, "Standalone") });
            Type groupType = group.GetType();
            int total = (int)groupType.GetMethod("GetTotalCount", Members).Invoke(group, null);
            int builtIn = (int)groupType.GetMethod("GetBuiltinCount", Members).Invoke(group, null);
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
            Type enumType = assembly.GetType("UnityEditor.GameViewSizeType");
            object size = Activator.CreateInstance(sizeType, Members, null,
                new object[] { Enum.Parse(enumType, "FixedResolution"), Width, Height, "Phase04 temporary evidence" }, null);
            groupType.GetMethod("AddCustomSize", Members).Invoke(group, new[] { size });
            customIndex = total - builtIn;
            fixedIndex = total;
            index.SetValue(view, total);
            view.Repaint();
        }

        public static void FocusFixedGameView()
        {
            if (view == null) return;
            view.GetType().GetProperty("selectedSizeIndex", Members).SetValue(view, fixedIndex);
            view.Focus();
            view.Repaint();
        }

        public static void CloseGameView()
        {
            if (view != null)
            {
                view.GetType().GetProperty("selectedSizeIndex", Members).SetValue(view, previousIndex);
                if (group != null && customIndex >= 0) group.GetType().GetMethod("RemoveCustomSize", Members).Invoke(group, new object[] { customIndex });
                view.Close();
            }
            view = null; group = null; customIndex = -1;
        }

        private static int Number(string name, int fallback) => int.TryParse(Value(name), out int n) && n > 0 ? n : fallback;
        private static string Value(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
