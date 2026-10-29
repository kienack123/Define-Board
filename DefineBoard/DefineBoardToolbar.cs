#if UNITY_EDITOR
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using System;
using System.Reflection;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
#else
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
#endif

namespace DefineBoard
{
#if !UNITY_6000_5_OR_NEWER
    [InitializeOnLoad]
#endif
    internal static class DefineBoardToolbar
    {
        private const string ELEMENT_PATH = "Define Board/Defines";
        private const string LABEL = "Defines";
        private const string TOOLTIP = "Open Define Board (Tools/Define Board/Open Window)";

#if UNITY_6000_5_OR_NEWER
        [MainToolbarElement(ELEMENT_PATH,
            defaultDockPosition = MainToolbarDockPosition.Right,
            defaultDockIndex = 0)]
        public static MainToolbarElement CreateButton()
        {
            return new MainToolbarButton(new MainToolbarContent(LABEL, TOOLTIP), DefineBoardWindow.Open);
        }

        [MenuItem("Tools/Define Board/Show Toolbar Button", false, 20)]
        private static void ShowToolbarButton()
        {
            Type mainToolbar = typeof(MainToolbarElement).Assembly.GetType("UnityEditor.Toolbars.MainToolbar");
            MethodInfo tryGetOverlay = mainToolbar == null
                ? null
                : mainToolbar.GetMethod("TryGetOverlay", BindingFlags.NonPublic | BindingFlags.Static);

            object[] args = { ELEMENT_PATH, null };
            Overlay overlay = tryGetOverlay != null && (bool)tryGetOverlay.Invoke(null, args)
                ? args[1] as Overlay
                : null;

            if (overlay == null)
            {
                Debug.LogWarning("[Define Board] The toolbar button is not part of the toolbar yet. Unity only creates " +
                                 "toolbar elements when the toolbar is rebuilt, so restart the Editor once, or enable \"" +
                                 ELEMENT_PATH + "\" from the overflow menu at the right end of the main toolbar.");
                return;
            }

            overlay.displayed = true;
            Debug.Log("[Define Board] Toolbar button is now shown at the top right.");
        }
#else
        private const string CONTAINER_NAME = "DefineBoard_ToolbarButton";
        private const string ZONE_NAME = "ToolbarZoneRightAlign";
        private const double CHECK_INTERVAL = 1.0;
        private const double WARN_AFTER = 15.0;
        private const float BUTTON_WIDTH = 66f;
        private const float BUTTON_HEIGHT = 20f;

        private static readonly GUIContent CONTENT = new GUIContent(LABEL, TOOLTIP);

        private static IMGUIContainer _container;
        private static double _nextCheck;
        private static double _warnTime;
        private static bool _warned;

        static DefineBoardToolbar()
        {
            _warnTime = EditorApplication.timeSinceStartup + WARN_AFTER;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextCheck) return;

            _nextCheck = now + CHECK_INTERVAL;
            if (_container != null && _container.panel != null) return;

            VisualElement zone = FindZone();
            if (zone == null)
            {
                if (!_warned && now > _warnTime)
                {
                    _warned = true;
                    Debug.LogWarning("[Define Board] Toolbar button was not installed: " + ZONE_NAME +
                                     " not found. The window is still available at Tools/Define Board/Open Window.");
                }

                return;
            }

            VisualElement existing = zone.Q(CONTAINER_NAME);
            if (existing != null)
            {
                _container = existing as IMGUIContainer;
                return;
            }

            _container = new IMGUIContainer(DrawButton);
            _container.name = CONTAINER_NAME;
            _container.style.width = BUTTON_WIDTH;
            _container.style.height = BUTTON_HEIGHT;
            _container.style.alignSelf = Align.Center;
            _container.style.marginRight = 6f;
            zone.Insert(0, _container);
        }

        private static VisualElement FindZone()
        {
            Type toolbarType = Type.GetType("UnityEditor.Toolbar, UnityEditor");
            if (toolbarType == null) return null;

            UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            if (toolbars == null || toolbars.Length == 0) return null;

            FieldInfo rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            VisualElement root = rootField == null ? null : rootField.GetValue(toolbars[0]) as VisualElement;
            return root == null ? null : root.Q(ZONE_NAME);
        }

        private static void DrawButton()
        {
            if (GUILayout.Button(CONTENT, EditorStyles.toolbarButton, GUILayout.Width(BUTTON_WIDTH), GUILayout.Height(BUTTON_HEIGHT)))
                DefineBoardWindow.Open();
        }
#endif
    }
}
#endif
