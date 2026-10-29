#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif

namespace DefineBoard
{
    public sealed class DefineBoardWindow : EditorWindow
    {
        private const string PREF_PREFIX = "DefineBoard.";
        private const string PREF_GROUP = PREF_PREFIX + "group";
        private const string PREF_SUFFIX = PREF_PREFIX + "suffix";
        private const string PREF_FILTER = PREF_PREFIX + "filter";
        private const string PREF_SECTION = PREF_PREFIX + "section";
        private const string CACHE_FILE = "DefineBoard.cache";
        private const string CTRL_SEARCH = "defineBoardSearch";
        private const string CTRL_ADD = "defineBoardAdd";
        private const int MAX_SITES = 40;
        private const int SECTION_COUNT = 4;

        private const int SECTION_SETTINGS = 0;
        private const int SECTION_ADDED = 1;
        private const int SECTION_CODE = 2;
        private const int SECTION_BUILT_IN = 3;

        private static readonly string[] SECTION_TITLES =
        {
            "In Player Settings", "Added, not applied", "Used in code", "Unity built-in",
        };

        private static readonly string[] FILTER_LABELS = { "All", "On", "Off", "Changed" };

        private static readonly BuildTargetGroup[] COMMON_GROUPS =
        {
            BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS, BuildTargetGroup.WebGL,
        };

        private static readonly string[] BUILT_IN_PREFIXES =
        {
            "UNITY_", "ENABLE_", "PLATFORM_", "NET_", "NETSTANDARD", "CSHARP_", "INCLUDE_",
        };

        private static readonly string[] BUILT_IN_NAMES = { "DEBUG", "TRACE", "DEVELOPMENT_BUILD", "NETFX_CORE" };

        private static readonly Color ON_COLOR = new Color(0.18f, 0.62f, 0.31f);
        private static readonly Color ON_HOVER = new Color(0.24f, 0.75f, 0.39f);
        private static readonly Color OFF_COLOR = new Color(0.71f, 0.23f, 0.20f);
        private static readonly Color OFF_HOVER = new Color(0.85f, 0.31f, 0.27f);
        private static readonly Color AUTO_COLOR = new Color(0.42f, 0.44f, 0.47f);
        private static readonly Color PENDING_COLOR = new Color(0.96f, 0.75f, 0.20f);

        private static Texture2D _fill;

        private sealed class Usage
        {
            public int Count;
            public readonly List<string> Sites = new List<string>();
        }

        private sealed class Row
        {
            public string Name;
            public bool On;
            public bool OffForm;
            public bool InDefines;
            public bool BuiltIn;
            public bool Pending;
            public int Refs;
            public int Section;
            public string Tooltip;
        }

        [SerializeField] private int _groupIndex;
        [SerializeField] private int _filter;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private string _newSymbol = string.Empty;
        [SerializeField] private bool _suffixConvention = true;
        [SerializeField] private bool _showRaw;

        private readonly List<string> _tokens = new List<string>();
        private readonly HashSet<string> _appliedSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _extra = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<BuildTargetGroup> _groups = new List<BuildTargetGroup>();
        private readonly List<Row>[] _sections = new List<Row>[SECTION_COUNT];
        private readonly List<Row>[] _frame = new List<Row>[SECTION_COUNT];
        private readonly bool[] _expanded = new bool[SECTION_COUNT];
        private readonly bool[] _frameExpanded = new bool[SECTION_COUNT];
        private readonly GUIContent _content = new GUIContent();

        private bool _frameApplyBar;
        private bool _frameHelpBox;
        private bool _frameUnknownGroup;
        private bool _frameEmpty;
        private bool _frameScanned;
        private bool _frameShowRaw;

        private Dictionary<string, Usage> _usages = new Dictionary<string, Usage>(StringComparer.Ordinal);
        private string _appliedRaw = string.Empty;
        private string _pendingRaw = string.Empty;
        private string _diffSummary = string.Empty;
        private string _scanInfo = string.Empty;
        private string[] _groupLabels;
        private string[] _filterLabels;
        private BuildTargetGroup _activeGroup = BuildTargetGroup.Unknown;
        private int _onCount;
        private int _pendingCount;
        private bool _rowsDirty = true;
        private bool _scanned;

        private GUIStyle _pillStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _nameDimStyle;
        private GUIStyle _countStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _placeholderStyle;
        private GUIStyle _badgeStyle;

        private bool Dirty { get { return !string.Equals(_pendingRaw, _appliedRaw, StringComparison.Ordinal); } }

        private float RowHeight { get { return EditorGUIUtility.singleLineHeight + 5f; } }

        private BuildTargetGroup CurrentGroup
        {
            get { return _groupIndex >= 0 && _groupIndex < _groups.Count ? _groups[_groupIndex] : BuildTargetGroup.Unknown; }
        }

        [MenuItem("Tools/Define Board/Open Window", false, 0)]
        public static void Open()
        {
            DefineBoardWindow window = GetWindow<DefineBoardWindow>(false, "Define Board", true);
            window.minSize = new Vector2(520f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;

            for (int i = 0; i < SECTION_COUNT; i++)
            {
                _sections[i] = new List<Row>();
                _frame[i] = new List<Row>();
                _expanded[i] = EditorPrefs.GetBool(PREF_SECTION + i, i != SECTION_BUILT_IN);
                _frameExpanded[i] = _expanded[i];
            }

            _suffixConvention = EditorPrefs.GetBool(PREF_SUFFIX, true);
            _filter = Mathf.Clamp(EditorPrefs.GetInt(PREF_FILTER, 0), 0, FILTER_LABELS.Length - 1);

            RebuildGroups();
            int stored = EditorPrefs.GetInt(PREF_GROUP, (int)ActiveGroup());
            _groupIndex = Mathf.Max(0, _groups.IndexOf((BuildTargetGroup)stored));

            ReadFromPlayerSettings();
            LoadCache();
        }

        private void OnFocus()
        {
            RebuildGroups();
            if (!Dirty) ReadFromPlayerSettings();
        }

        private void OnDestroy()
        {
            if (!Dirty) return;

            bool apply = EditorUtility.DisplayDialog(
                "Define Board",
                "Changes for " + CurrentGroup + " were never applied.\n\n" + _diffSummary,
                "Apply", "Discard");
            if (apply) Apply();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (Event.current.type == EventType.Layout) SnapshotFrame();

            DrawToolbar();
            DrawFilterBar();
            DrawApplyBar();
            DrawBody();
            DrawFooter();

            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        private void SnapshotFrame()
        {
            if (_rowsDirty) RebuildRows();

            int visible = 0;
            for (int i = 0; i < SECTION_COUNT; i++)
            {
                List<Row> rows = _sections[i];
                List<Row> frame = _frame[i];
                frame.Clear();

                for (int r = 0; r < rows.Count; r++)
                    if (IsVisible(rows[r])) frame.Add(rows[r]);

                _frameExpanded[i] = _expanded[i];
                visible += frame.Count;
            }

            _frameApplyBar = Dirty;
            _frameUnknownGroup = CurrentGroup == BuildTargetGroup.Unknown;
            _frameHelpBox = !_frameUnknownGroup && _activeGroup != CurrentGroup;
            _frameEmpty = visible == 0;
            _frameScanned = _scanned;
            _frameShowRaw = _showRaw;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            int group = EditorGUILayout.Popup(_groupIndex, _groupLabels, EditorStyles.toolbarPopup, GUILayout.Width(150f));
            if (group != _groupIndex) ChangeGroup(group);

            GUILayout.FlexibleSpace();

            GUI.SetNextControlName(CTRL_SEARCH);
            string search = GUILayout.TextField(_search, EditorStyles.toolbarTextField, GUILayout.MinWidth(90f), GUILayout.MaxWidth(190f));
            Rect searchRect = GUILayoutUtility.GetLastRect();
            if (search != _search)
            {
                _search = search;
                Repaint();
            }

            if (_search.Length == 0 && GUI.GetNameOfFocusedControl() != CTRL_SEARCH)
                GUI.Label(searchRect, "Search", _placeholderStyle);

            using (new EditorGUI.DisabledScope(_search.Length == 0))
            {
                if (GUILayout.Button(new GUIContent("x", "Clear search"), EditorStyles.toolbarButton, GUILayout.Width(22f)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                    Repaint();
                }
            }

            if (GUILayout.Button(new GUIContent("Options", "Rescan, reload, conventions"), EditorStyles.toolbarDropDown, GUILayout.Width(66f)))
                ShowOptionsMenu();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFilterBar()
        {
            Rect line = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
            Fill(line, HeaderColor());

            Rect tabs = new Rect(line.x + 4f, line.y + 2f, Mathf.Min(300f, line.width - 8f), line.height - 4f);
            int filter = GUI.Toolbar(tabs, _filter, _filterLabels, EditorStyles.miniButtonMid);
            if (filter != _filter)
            {
                _filter = filter;
                EditorPrefs.SetInt(PREF_FILTER, filter);
                Repaint();
            }

            if (line.width > 430f)
            {
                Rect info = new Rect(tabs.xMax + 8f, line.y, line.width - tabs.width - 16f, line.height);
                GUI.Label(info, _scanInfo, _countStyle);
            }
        }

        private void DrawApplyBar()
        {
            if (!_frameApplyBar) return;

            Rect bar = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 10f);
            Fill(bar, new Color(PENDING_COLOR.r, PENDING_COLOR.g, PENDING_COLOR.b, EditorGUIUtility.isProSkin ? 0.16f : 0.28f));
            Fill(new Rect(bar.x, bar.y, 3f, bar.height), PENDING_COLOR);

            float buttons = 168f;
            Rect text = new Rect(bar.x + 10f, bar.y, Mathf.Max(40f, bar.width - buttons - 16f), bar.height);
            GUI.Label(text, _diffSummary, _nameStyle);

            Rect revert = new Rect(bar.xMax - buttons, bar.y + 4f, 66f, bar.height - 8f);
            if (GUI.Button(revert, new GUIContent("Revert", "Drop the pending changes"))) ReadFromPlayerSettings();

            Rect apply = new Rect(revert.xMax + 6f, bar.y + 4f, 92f, bar.height - 8f);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = ON_COLOR;
            if (GUI.Button(apply, new GUIContent("Apply", "Write to Player Settings, Unity recompiles scripts")))
            {
                GUI.backgroundColor = previous;
                Apply();
                return;
            }

            GUI.backgroundColor = previous;
        }

        private void DrawBody()
        {
            if (_frameUnknownGroup)
            {
                EditorGUILayout.HelpBox("Unsupported build target group.", MessageType.Warning);
                return;
            }

            if (_frameHelpBox)
            {
                EditorGUILayout.HelpBox(
                    "Editing " + CurrentGroup + " while the active build target is " + _activeGroup +
                    ". These defines only reach the Editor after switching platform.",
                    MessageType.Info);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int section = 0; section < SECTION_COUNT; section++) DrawSection(section);

            if (_frameEmpty) DrawEmptyState();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSection(int section)
        {
            List<Row> rows = _frame[section];
            if (rows.Count == 0) return;

            Rect header = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 6f);
            Fill(header, HeaderColor());

            Rect foldout = new Rect(header.x + 6f, header.y + 3f, Mathf.Max(40f, header.width - 70f), header.height - 6f);
            bool expanded = EditorGUI.Foldout(foldout, _frameExpanded[section], SECTION_TITLES[section], true, _sectionStyle);
            if (expanded != _expanded[section])
            {
                _expanded[section] = expanded;
                EditorPrefs.SetBool(PREF_SECTION + section, expanded);
                Repaint();
            }

            Rect count = new Rect(header.xMax - 60f, header.y, 54f, header.height);
            GUI.Label(count, rows.Count.ToString(), _countStyle);

            if (!_frameExpanded[section]) return;

            for (int i = 0; i < rows.Count; i++) DrawRow(rows[i], i);
        }

        private void DrawRow(Row row, int index)
        {
            Rect line = EditorGUILayout.GetControlRect(false, RowHeight);
            bool hoverRow = line.Contains(Event.current.mousePosition);

            if ((index & 1) == 1) Fill(line, ZebraColor());
            if (hoverRow) Fill(line, HoverColor());
            if (row.Pending) Fill(new Rect(line.x, line.y, 3f, line.height), PENDING_COLOR);

            float pillWidth = 46f;
            Rect pill = new Rect(line.x + 8f, line.y + 2f, pillWidth, line.height - 4f);
            bool auto = row.Section == SECTION_BUILT_IN;
            bool hoverPill = !auto && pill.Contains(Event.current.mousePosition);

            Color color = auto ? AUTO_COLOR : row.On
                ? (hoverPill ? ON_HOVER : ON_COLOR)
                : (hoverPill ? OFF_HOVER : OFF_COLOR);
            Fill(pill, color);
            GUI.Label(pill, auto ? "AUTO" : row.On ? "ON" : "OFF", _pillStyle);

            if (!auto)
            {
                EditorGUIUtility.AddCursorRect(pill, MouseCursor.Link);
                if (GUI.Button(pill, GUIContent.none, GUIStyle.none)) Toggle(row);
            }

            float refsWidth = 46f;
            float nameLeft = pill.xMax + 10f;
            float nameWidth = Mathf.Max(30f, line.xMax - refsWidth - 10f - nameLeft);
            Rect name = new Rect(nameLeft, line.y, nameWidth, line.height);

            _content.text = row.Name;
            _content.tooltip = row.Tooltip;
            GUIStyle style = row.InDefines ? _nameStyle : _nameDimStyle;
            GUI.Label(name, _content, style);

            if (row.OffForm)
            {
                float used = Mathf.Min(nameWidth, style.CalcSize(new GUIContent(row.Name)).x) + 6f;
                Rect badge = new Rect(name.x + used, line.y + 4f, 16f, line.height - 8f);
                if (badge.xMax < name.xMax)
                {
                    Fill(badge, new Color(PENDING_COLOR.r, PENDING_COLOR.g, PENDING_COLOR.b, 0.28f));
                    GUI.Label(badge, "1", _badgeStyle);
                }
            }

            Rect refs = new Rect(line.xMax - refsWidth - 6f, line.y, refsWidth, line.height);
            if (row.Refs > 0)
            {
                EditorGUIUtility.AddCursorRect(refs, MouseCursor.Link);
                _content.text = row.Refs.ToString();
                _content.tooltip = "Show where this symbol is used";
                GUI.Label(refs, _content, _countStyle);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && refs.Contains(Event.current.mousePosition))
                {
                    ShowUsageMenu(row.Name);
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.ContextClick && hoverRow)
            {
                ShowRowMenu(row);
                Event.current.Use();
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space();

            if (!_frameScanned)
            {
                EditorGUILayout.LabelField("No symbol list yet.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Scan project scripts", GUILayout.Width(170f))) Rescan();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.LabelField("Nothing matches the current filter.", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            GUILayout.Label("Add", EditorStyles.miniLabel, GUILayout.Width(26f));
            GUI.SetNextControlName(CTRL_ADD);
            _newSymbol = EditorGUILayout.TextField(_newSymbol);

            bool valid = IsValidSymbol(_newSymbol);
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                         GUI.GetNameOfFocusedControl() == CTRL_ADD;

            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button(new GUIContent("Add", "Add the symbol and turn it on"), GUILayout.Width(56f)) || (enter && valid))
                {
                    if (enter) Event.current.Use();
                    AddSymbol();
                }
            }

            EditorGUILayout.EndHorizontal();

            bool showRaw = EditorGUILayout.Foldout(_frameShowRaw, "Raw string", true);
            if (showRaw != _showRaw)
            {
                _showRaw = showRaw;
                Repaint();
            }

            if (!_frameShowRaw) return;

            EditorGUILayout.SelectableLabel(_pendingRaw, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(44f));
        }

        private void EnsureStyles()
        {
            if (_pillStyle != null) return;

            _pillStyle = new GUIStyle(EditorStyles.miniLabel);
            _pillStyle.alignment = TextAnchor.MiddleCenter;
            _pillStyle.fontStyle = FontStyle.Bold;
            _pillStyle.normal.textColor = Color.white;
            _pillStyle.padding = new RectOffset(0, 0, 0, 0);

            _nameStyle = new GUIStyle(EditorStyles.label);
            _nameStyle.alignment = TextAnchor.MiddleLeft;

            _nameDimStyle = new GUIStyle(_nameStyle);
            Color dim = _nameStyle.normal.textColor;
            dim.a = 0.62f;
            _nameDimStyle.normal.textColor = dim;

            _countStyle = new GUIStyle(EditorStyles.miniLabel);
            _countStyle.alignment = TextAnchor.MiddleRight;

            _sectionStyle = new GUIStyle(EditorStyles.foldout);
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.fontSize = Mathf.Max(9, EditorStyles.miniLabel.fontSize);

            _placeholderStyle = new GUIStyle(EditorStyles.miniLabel);
            _placeholderStyle.alignment = TextAnchor.MiddleLeft;
            _placeholderStyle.padding = new RectOffset(14, 0, 0, 0);
            Color ghost = _placeholderStyle.normal.textColor;
            ghost.a = 0.45f;
            _placeholderStyle.normal.textColor = ghost;

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel);
            _badgeStyle.alignment = TextAnchor.MiddleCenter;
            _badgeStyle.padding = new RectOffset(0, 0, 0, 0);

            _filterLabels = new string[FILTER_LABELS.Length];
            Array.Copy(FILTER_LABELS, _filterLabels, FILTER_LABELS.Length);
        }

        private static Color HeaderColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.07f);
        }

        private static Color ZebraColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.022f) : new Color(0f, 0f, 0f, 0.028f);
        }

        private static Color HoverColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f);
        }

        private static void Fill(Rect rect, Color color)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            if (_fill == null)
            {
                _fill = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _fill.SetPixel(0, 0, Color.white);
                _fill.Apply();
                _fill.hideFlags = HideFlags.HideAndDontSave;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _fill);
            GUI.color = previous;
        }

        private bool IsVisible(Row row)
        {
            if (_filter == 1 && !row.On) return false;
            if (_filter == 2 && row.On) return false;
            if (_filter == 3 && !row.Pending) return false;
            if (_search.Length > 0 && row.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void ShowOptionsMenu()
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Rescan project scripts"), false, Rescan);
            menu.AddItem(new GUIContent("Reload from Player Settings"), false, () =>
            {
                if (!Dirty || EditorUtility.DisplayDialog("Define Board", "Discard the pending changes?", "Discard", "Keep"))
                    ReadFromPlayerSettings();
            });

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Turn off by appending 1"), _suffixConvention, () =>
            {
                _suffixConvention = !_suffixConvention;
                EditorPrefs.SetBool(PREF_SUFFIX, _suffixConvention);
                _rowsDirty = true;
                Repaint();
            });

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Copy define string"), false, () => EditorGUIUtility.systemCopyBuffer = _pendingRaw);
            menu.AddItem(new GUIContent("Open Player Settings"), false, OpenPlayerSettings);

            menu.ShowAsContext();
        }

        private static void OpenPlayerSettings()
        {
#if UNITY_2018_3_OR_NEWER
            SettingsService.OpenProjectSettings("Project/Player");
#else
            EditorApplication.ExecuteMenuItem("Edit/Project Settings/Player");
#endif
        }

        private void ShowRowMenu(Row row)
        {
            GenericMenu menu = new GenericMenu();

            if (row.Section == SECTION_BUILT_IN)
            {
                menu.AddDisabledItem(new GUIContent("Defined by Unity, not editable here"));
                menu.AddSeparator(string.Empty);
            }
            else
            {
                menu.AddItem(new GUIContent(row.On ? "Turn off" : "Turn on"), false, () => Toggle(row));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy name"), false, () => EditorGUIUtility.systemCopyBuffer = row.Name);

            if (row.Refs > 0) menu.AddItem(new GUIContent("Log usages to Console"), false, () => LogUsages(row.Name));
            else menu.AddDisabledItem(new GUIContent("Log usages to Console"));

            if (row.InDefines) menu.AddItem(new GUIContent("Remove from define string"), false, () => RemoveSymbol(row.Name));
            else menu.AddDisabledItem(new GUIContent("Remove from define string"));

            menu.ShowAsContext();
        }

        private void ShowUsageMenu(string name)
        {
            Usage usage;
            if (!_usages.TryGetValue(name, out usage) || usage.Sites.Count == 0) return;

            GenericMenu menu = new GenericMenu();
            for (int i = 0; i < usage.Sites.Count; i++)
            {
                string site = usage.Sites[i];
                menu.AddItem(new GUIContent(ShortSite(site)), false, () => OpenSite(site));
            }

            if (usage.Count > usage.Sites.Count)
            {
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("and " + (usage.Count - usage.Sites.Count) + " more"));
            }

            menu.ShowAsContext();
        }

        private void RebuildGroups()
        {
            BuildTargetGroup active = ActiveGroup();
            if (_groupLabels != null && active == _activeGroup) return;

            BuildTargetGroup previous = CurrentGroup;
            _activeGroup = active;
            _groups.Clear();

            for (int i = 0; i < COMMON_GROUPS.Length; i++)
                if (IsSupported(COMMON_GROUPS[i])) _groups.Add(COMMON_GROUPS[i]);

            if (active != BuildTargetGroup.Unknown && !_groups.Contains(active)) _groups.Insert(0, active);

            _groupLabels = new string[_groups.Count];
            for (int i = 0; i < _groups.Count; i++)
                _groupLabels[i] = _groups[i] == active ? _groups[i] + "  (active)" : _groups[i].ToString();

            int index = _groups.IndexOf(previous);
            _groupIndex = Mathf.Clamp(index < 0 ? 0 : index, 0, Mathf.Max(0, _groups.Count - 1));
        }

        private static BuildTargetGroup ActiveGroup()
        {
            return EditorUserBuildSettings.selectedBuildTargetGroup;
        }

        private static bool IsSupported(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Unknown) return false;
#if UNITY_2021_2_OR_NEWER
            try
            {
                NamedBuildTarget.FromBuildTargetGroup(group);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
#else
            return true;
#endif
        }

        private static string GetDefines(BuildTargetGroup group)
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group)) ?? string.Empty;
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? string.Empty;
#endif
        }

        private static void SetDefines(BuildTargetGroup group, string value)
        {
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group), value);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, value);
#endif
        }

        private void ChangeGroup(int index)
        {
            if (Dirty && !EditorUtility.DisplayDialog("Define Board",
                    "Discard the pending changes for " + CurrentGroup + "?", "Discard", "Keep"))
                return;

            _groupIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _groups.Count - 1));
            EditorPrefs.SetInt(PREF_GROUP, (int)CurrentGroup);
            ReadFromPlayerSettings();
        }

        private void ReadFromPlayerSettings()
        {
            string raw = string.Empty;
            BuildTargetGroup group = CurrentGroup;

            if (group != BuildTargetGroup.Unknown)
            {
                try { raw = GetDefines(group); }
                catch (Exception e)
                {
                    Debug.LogError("[Define Board] Cannot read defines for " + group + ": " + e.Message);
                    raw = string.Empty;
                }
            }

            _tokens.Clear();
            _appliedSet.Clear();

            string[] parts = raw.Split(new[] { ';', ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (_tokens.Contains(parts[i])) continue;
                _tokens.Add(parts[i]);
                _appliedSet.Add(parts[i]);
            }

            _appliedRaw = string.Join(";", _tokens.ToArray());
            _pendingRaw = _appliedRaw;
            _rowsDirty = true;
            Repaint();
        }

        private void Apply()
        {
            BuildTargetGroup group = CurrentGroup;
            if (group == BuildTargetGroup.Unknown) return;

            if (EditorApplication.isPlaying && !EditorUtility.DisplayDialog("Define Board",
                    "Play Mode is running. Applying forces a script recompile and the domain reload can break the current session.\n\nApply anyway?",
                    "Apply", "Cancel"))
                return;

            string value = string.Join(";", _tokens.ToArray());
            try
            {
                SetDefines(group, value);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError("[Define Board] Apply failed for " + group + ": " + e.Message);
                return;
            }

            Debug.Log("[Define Board] " + group + " = " + value);
            ReadFromPlayerSettings();
        }

        private void AddSymbol()
        {
            string name = _newSymbol.Trim();
            if (!IsValidSymbol(name)) return;

            _extra.Add(name);
            SetOn(name);
            _newSymbol = string.Empty;
            _search = string.Empty;
            GUI.FocusControl(null);
            Repaint();
        }

        private void Toggle(Row row)
        {
            if (_tokens.Contains(row.Name)) SetOff(row.Name);
            else SetOn(row.Name);

            GUI.FocusControl(null);
            Repaint();
        }

        private void SetOn(string name)
        {
            string off = name + "1";
            bool placed = _tokens.Contains(name);

            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(_tokens[i], off, StringComparison.Ordinal)) continue;

                if (placed)
                {
                    _tokens.RemoveAt(i);
                }
                else
                {
                    _tokens[i] = name;
                    placed = true;
                }
            }

            if (!placed) _tokens.Add(name);
            MarkChanged();
        }

        private void SetOff(string name)
        {
            string off = name + "1";
            bool known = _appliedSet.Contains(name) || _appliedSet.Contains(off);
            bool keepMarker = _suffixConvention && known;
            bool placed = _tokens.Contains(off);

            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(_tokens[i], name, StringComparison.Ordinal)) continue;

                if (keepMarker && !placed)
                {
                    _tokens[i] = off;
                    placed = true;
                }
                else
                {
                    _tokens.RemoveAt(i);
                }
            }

            MarkChanged();
        }

        private void RemoveSymbol(string name)
        {
            string off = name + "1";
            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_tokens[i], name, StringComparison.Ordinal) ||
                    string.Equals(_tokens[i], off, StringComparison.Ordinal))
                    _tokens.RemoveAt(i);
            }

            _extra.Remove(name);
            MarkChanged();
            Repaint();
        }

        private void MarkChanged()
        {
            _pendingRaw = string.Join(";", _tokens.ToArray());
            _rowsDirty = true;
        }

        private void RebuildRows()
        {
            Dictionary<string, Row> map = new Dictionary<string, Row>(StringComparer.Ordinal);

            for (int i = 0; i < _tokens.Count; i++)
            {
                bool offForm;
                string name = BaseName(_tokens[i], out offForm);
                Row row = GetOrCreate(map, name);
                row.InDefines = true;
                if (offForm) row.OffForm = true;
                else row.On = true;
            }

            foreach (KeyValuePair<string, Usage> pair in _usages)
                GetOrCreate(map, pair.Key).Refs = pair.Value.Count;

            foreach (string name in _extra) GetOrCreate(map, name);

            for (int i = 0; i < SECTION_COUNT; i++) _sections[i].Clear();

            _onCount = 0;
            _pendingCount = 0;
            List<string> added = new List<string>();
            List<string> removed = new List<string>();

            foreach (KeyValuePair<string, Row> pair in map)
            {
                Row row = pair.Value;
                row.BuiltIn = IsBuiltIn(row.Name);
                row.Pending = row.On != _appliedSet.Contains(row.Name);

                bool applied = _appliedSet.Contains(row.Name) || _appliedSet.Contains(row.Name + "1");
                if (applied) row.Section = SECTION_SETTINGS;
                else if (row.InDefines) row.Section = SECTION_ADDED;
                else if (row.BuiltIn) row.Section = SECTION_BUILT_IN;
                else if (row.Refs > 0) row.Section = SECTION_CODE;
                else row.Section = SECTION_ADDED;

                if (row.Section == SECTION_BUILT_IN) row.On = false;
                if (row.On) _onCount++;

                if (row.Pending)
                {
                    _pendingCount++;
                    if (row.On) added.Add("+" + row.Name);
                    else removed.Add("-" + row.Name);
                }

                row.Tooltip = BuildTooltip(row);
                _sections[row.Section].Add(row);
            }

            for (int i = 0; i < SECTION_COUNT; i++)
                _sections[i].Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            BuildFilterLabels();
            BuildDiffSummary(added, removed);
            _rowsDirty = false;
        }

        private void BuildFilterLabels()
        {
            int total = 0;
            for (int i = 0; i < SECTION_COUNT; i++) total += _sections[i].Count;

            if (_filterLabels == null || _filterLabels.Length != FILTER_LABELS.Length)
                _filterLabels = new string[FILTER_LABELS.Length];

            _filterLabels[0] = FILTER_LABELS[0] + "  " + total;
            _filterLabels[1] = FILTER_LABELS[1] + "  " + _onCount;
            _filterLabels[2] = FILTER_LABELS[2] + "  " + Mathf.Max(0, total - _onCount);
            _filterLabels[3] = FILTER_LABELS[3] + "  " + _pendingCount;
        }

        private void BuildDiffSummary(List<string> added, List<string> removed)
        {
            if (!Dirty)
            {
                _diffSummary = string.Empty;
                return;
            }

            added.Sort(StringComparer.OrdinalIgnoreCase);
            removed.Sort(StringComparer.OrdinalIgnoreCase);

            List<string> all = new List<string>(added);
            all.AddRange(removed);

            if (all.Count == 0)
            {
                _diffSummary = "Define string edited";
                return;
            }

            StringBuilder sb = new StringBuilder();
            int max = Mathf.Min(all.Count, 4);
            for (int i = 0; i < max; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append(all[i]);
            }

            if (all.Count > max) sb.Append("   and ").Append(all.Count - max).Append(" more");
            _diffSummary = sb.ToString();
        }

        private static Row GetOrCreate(Dictionary<string, Row> map, string name)
        {
            Row row;
            if (map.TryGetValue(name, out row)) return row;

            row = new Row();
            row.Name = name;
            map.Add(name, row);
            return row;
        }

        private string BuildTooltip(Row row)
        {
            StringBuilder sb = new StringBuilder();

            if (row.Section == SECTION_BUILT_IN)
                sb.Append("Unity defines this symbol automatically, it is not part of the define string");
            else
                sb.Append(row.On ? "ON for " : "OFF for ").Append(CurrentGroup);

            if (row.OffForm) sb.Append("\nStored as ").Append(row.Name).Append("1, which no #if matches");
            if (row.Pending) sb.Append("\nPending, press Apply to write it to Player Settings");

            Usage usage;
            if (_usages.TryGetValue(row.Name, out usage) && usage.Sites.Count > 0)
            {
                sb.Append("\n");
                int max = Mathf.Min(usage.Sites.Count, 8);
                for (int i = 0; i < max; i++) sb.Append("\n").Append(usage.Sites[i]);
                if (usage.Count > max) sb.Append("\nand ").Append(usage.Count - max).Append(" more");
            }

            return sb.ToString();
        }

        private string BaseName(string token, out bool offForm)
        {
            offForm = false;
            if (!_suffixConvention || token.Length < 3 || token[token.Length - 1] != '1') return token;

            string baseName = token.Substring(0, token.Length - 1);
            char last = baseName[baseName.Length - 1];
            if (last == '_' || (last >= '0' && last <= '9')) return token;

            offForm = true;
            return baseName;
        }

        private static bool IsBuiltIn(string name)
        {
            for (int i = 0; i < BUILT_IN_NAMES.Length; i++)
                if (string.Equals(name, BUILT_IN_NAMES[i], StringComparison.Ordinal)) return true;

            for (int i = 0; i < BUILT_IN_PREFIXES.Length; i++)
                if (name.StartsWith(BUILT_IN_PREFIXES[i], StringComparison.Ordinal)) return true;

            return false;
        }

        private static bool IsValidSymbol(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            string name = value.Trim();
            if (name.Length == 0) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;

            for (int i = 1; i < name.Length; i++)
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;

            return true;
        }

        private static string ShortSite(string site)
        {
            int colon = site.LastIndexOf(':');
            if (colon < 0) return site;

            string path = site.Substring(0, colon);
            int slash = path.LastIndexOf('/');
            return (slash < 0 ? path : path.Substring(slash + 1)) + site.Substring(colon);
        }

        private static void OpenSite(string site)
        {
            int colon = site.LastIndexOf(':');
            if (colon < 0) return;

            string path = site.Substring(0, colon);
            int lineNumber;
            int.TryParse(site.Substring(colon + 1), out lineNumber);

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                Debug.LogWarning("[Define Board] Script not found: " + path);
                return;
            }

            AssetDatabase.OpenAsset(asset, Mathf.Max(1, lineNumber));
        }

        private void LogUsages(string name)
        {
            Usage usage;
            if (!_usages.TryGetValue(name, out usage)) return;

            StringBuilder sb = new StringBuilder();
            sb.Append("[Define Board] ").Append(name).Append(" - ").Append(usage.Count).Append(" usage(s)");
            for (int i = 0; i < usage.Sites.Count; i++) sb.Append("\n").Append(usage.Sites[i]);
            if (usage.Count > usage.Sites.Count) sb.Append("\nand ").Append(usage.Count - usage.Sites.Count).Append(" more");

            Debug.Log(sb.ToString());
        }

        private static string CachePath()
        {
            DirectoryInfo root = Directory.GetParent(Application.dataPath);
            string library = root == null ? Application.dataPath : Path.Combine(root.FullName, "Library");
            return Path.Combine(library, CACHE_FILE).Replace('\\', '/');
        }

        private void LoadCache()
        {
            _usages = new Dictionary<string, Usage>(StringComparer.Ordinal);
            _scanned = false;
            _scanInfo = string.Empty;

            string path = CachePath();
            string raw = null;

            try
            {
                if (File.Exists(path)) raw = File.ReadAllText(path);
            }
            catch (Exception)
            {
                raw = null;
            }

            if (string.IsNullOrEmpty(raw))
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Rescan();
                return;
            }

            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string entry = lines[i].Trim();
                if (entry.Length == 0) continue;

                if (i == 0 && entry.StartsWith("#", StringComparison.Ordinal))
                {
                    _scanInfo = "scanned " + entry.Substring(1);
                    continue;
                }

                string[] parts = entry.Split('|');
                if (parts.Length < 2) continue;

                Usage usage = new Usage();
                int.TryParse(parts[1], out usage.Count);
                for (int p = 2; p < parts.Length; p++) usage.Sites.Add(parts[p]);
                _usages[parts[0]] = usage;
            }

            _scanned = _usages.Count > 0;
            _rowsDirty = true;
        }

        private void SaveCache()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('#').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append('\n');

            foreach (KeyValuePair<string, Usage> pair in _usages)
            {
                sb.Append(pair.Key).Append('|').Append(pair.Value.Count);
                for (int i = 0; i < pair.Value.Sites.Count; i++) sb.Append('|').Append(pair.Value.Sites[i]);
                sb.Append('\n');
            }

            try { File.WriteAllText(CachePath(), sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("[Define Board] Cannot write cache: " + e.Message); }
        }

        private void Rescan()
        {
            _usages = new Dictionary<string, Usage>(StringComparer.Ordinal);

            string root = Application.dataPath.Replace('\\', '/');
            string[] files;

            try { files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
            catch (Exception e)
            {
                Debug.LogError("[Define Board] Scan failed: " + e.Message);
                return;
            }

            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if ((i & 127) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Define Board", "Scanning scripts for #if symbols", (float)i / Mathf.Max(1, files.Length)))
                        break;

                    ScanFile(files[i], root);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            SaveCache();
            _scanned = true;
            _scanInfo = "scanned " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _rowsDirty = true;
            Repaint();
        }

        private void ScanFile(string path, string root)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception) { return; }

            if (text.IndexOf('#') < 0) return;

            string normalized = path.Replace('\\', '/');
            string relative = normalized.Length > root.Length ? "Assets" + normalized.Substring(root.Length) : normalized;

            int index = 0;
            int line = 1;
            while (index < text.Length)
            {
                int end = text.IndexOf('\n', index);
                if (end < 0) end = text.Length;
                ScanDirective(text, index, end, relative, line);
                index = end + 1;
                line++;
            }
        }

        private void ScanDirective(string text, int start, int end, string relative, int line)
        {
            int p = start;
            while (p < end && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= end || text[p] != '#') return;

            p++;
            while (p < end && (text[p] == ' ' || text[p] == '\t')) p++;

            int keyword = p;
            while (p < end && char.IsLetter(text[p])) p++;
            int length = p - keyword;

            bool isIf = length == 2 && string.CompareOrdinal(text, keyword, "if", 0, 2) == 0;
            bool isElif = length == 4 && string.CompareOrdinal(text, keyword, "elif", 0, 4) == 0;
            if (!isIf && !isElif) return;

            while (p < end)
            {
                char c = text[p];
                if (c == '/' && p + 1 < end && (text[p + 1] == '/' || text[p + 1] == '*')) return;

                if (c == '_' || char.IsLetter(c))
                {
                    int from = p;
                    while (p < end && (text[p] == '_' || char.IsLetterOrDigit(text[p]))) p++;

                    string id = text.Substring(from, p - from);
                    if (!string.Equals(id, "true", StringComparison.Ordinal) &&
                        !string.Equals(id, "false", StringComparison.Ordinal))
                        AddUsage(id, relative, line);

                    continue;
                }

                p++;
            }
        }

        private void AddUsage(string symbol, string relative, int line)
        {
            Usage usage;
            if (!_usages.TryGetValue(symbol, out usage))
            {
                usage = new Usage();
                _usages.Add(symbol, usage);
            }

            usage.Count++;
            if (usage.Sites.Count < MAX_SITES) usage.Sites.Add(relative + ":" + line);
        }
    }
}
#endif
