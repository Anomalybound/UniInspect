using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniInspect
{
    public class UniInspectBrowser : EditorWindow, IHasCustomMenu
    {
        [MenuItem("UniInspect/Browser")]
        private static void Open()
        {
            window = (UniInspectBrowser) GetWindow(typeof(UniInspectBrowser));
            window.titleContent = new GUIContent("UniInspect Browser");
            window.minSize = new Vector2(350, 500);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void Reload()
        {
            goIcon = EditorGUIUtility.FindTexture("PrefabNormal Icon");
            soIcon = EditorGUIUtility.FindTexture("ScriptableObject Icon");

            var findTextureFunc = typeof(EditorGUIUtility).GetMethod("FindTexture",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (findTextureFunc != null)
            {
//                goIcon = findTextureFunc.Invoke(null, new[] {typeof(GameObject)}) as Texture2D;
                soIcon = findTextureFunc.Invoke(null, new[] {typeof(ScriptableObject)}) as Texture2D;
            }
        }

        private static UniInspectBrowser Window
        {
            get
            {
                if (window == null) { window = (UniInspectBrowser) GetWindow(typeof(UniInspectBrowser)); }

                return window;
            }
        }

        private static UniInspectBrowser window;

        private static Texture2D goIcon;
        private static Texture2D soIcon;

        private static GUIStyle line1Style = new GUIStyle();
        private static GUIStyle line2Style = new GUIStyle();
        private static GUIStyle centeredLabelStyle = new GUIStyle();
        private static GUIStyle objectLabelStyle = new GUIStyle();

        private static readonly GUIContent showGameObjectsTitle = new GUIContent("GameObjects");
        private static readonly GUIContent showScriptableObjectsTitle = new GUIContent("ScriptableObjects");

        private static bool showGameObjects
        {
            get { return EditorPrefs.GetBool("UniInspect.ShowGameObjects", true); }
            set { EditorPrefs.SetBool("UniInspect.ShowGameObjects", value); }
        }

        private static bool showScriptableObjects
        {
            get { return EditorPrefs.GetBool("UniInspect.ShowScriptableObjects", true); }
            set { EditorPrefs.SetBool("UniInspect.ShowScriptableObjects", value); }
        }

        private Vector2 _scrollPos;
        private string _filter;
        private int _pageSize = 10;
        private int _pageIndicator;
        private SearchField _searchField;
        private List<GameObject> _filteredGameObjects = new List<GameObject>();
        private List<ScriptableObject> _filteredScriptableObjects = new List<ScriptableObject>();
        private List<Object> _filteredObjects = new List<Object>();
        private List<Object> _hidedObjects = new List<Object>();
        private List<MonoBehaviour> _hidedMonoBehaviours = new List<MonoBehaviour>();

        private void OnEnable()
        {
            UniInspectEditor.CollectObjects();

            FilteredResults();

            SetupWidgets();
        }

        private void OnFocus()
        {
            UniInspectEditor.CollectObjects();

            FilteredResults();

            SetupWidgets();
        }

        private void OnGUI()
        {
            SearchBar();

            PagingTools();

            DrawItems();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(showGameObjectsTitle, showGameObjects, ToggleGameObjects);
            menu.AddItem(showScriptableObjectsTitle, showScriptableObjects, ToggleScriptableObjects);
        }

        private void DrawItems()
        {
            var start = _pageIndicator * _pageSize;
            var end = Mathf.Min(_filteredObjects.Count, start + _pageSize);

            if (_filteredObjects.Count <= 0) { return; }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPos))
            {
                using (new GUILayout.VerticalScope())
                {
                    for (var i = start; i < end; i++)
                    {
                        var obj = _filteredObjects[i];

                        if (obj is GameObject)
                        {
                            var go = obj as GameObject;
                            DrawGameObjectItem(go, UniInspectEditor.GameObjectInstances[go], i % _pageSize % 2 == 0);
                        }

                        if (obj is ScriptableObject)
                        {
                            var so = obj as ScriptableObject;
                            DrawScriptableObjectItem(so, i % _pageSize % 2 == 0);
                        }
                    }

                    _scrollPos = scroll.scrollPosition;
                }
            }
        }

        private void SetupWidgets()
        {
            var line1Color = new Color(0.78f, 0.78f, 0.78f);
            var line2Color = new Color(0.85f, 0.85f, 0.85f);

            line1Style.normal.background = MakeTex(1, 1, line1Color);
            line2Style.normal.background = MakeTex(1, 1, line2Color);

            centeredLabelStyle.fontSize = 14;
            centeredLabelStyle.fontStyle = FontStyle.Bold;
            centeredLabelStyle.alignment = TextAnchor.MiddleCenter;

            objectLabelStyle.fontSize = 16;
            objectLabelStyle.fontStyle = FontStyle.Bold;
            objectLabelStyle.alignment = TextAnchor.MiddleLeft;

            if (_searchField == null) { _searchField = new SearchField(); }
        }

        private void PagingTools()
        {
            using (new GUILayout.HorizontalScope())
            {
                _pageSize = EditorGUILayout.IntField(new GUIContent("Items per page"), _pageSize);
                _pageSize = Mathf.Max(1, _pageSize);

                GUILayout.FlexibleSpace();

                var maxPage = Mathf.FloorToInt(_filteredObjects.Count / (float) _pageSize);

                if (_filteredObjects.Count % _pageSize == 0) { --maxPage; }

                _pageIndicator = _filteredObjects.Count <= 0 ? -1 : Mathf.Clamp(_pageIndicator, 0, maxPage);

                using (new EditorGUI.DisabledScope(_pageIndicator <= 0))
                {
                    if (GUILayout.Button("<<", EditorStyles.toolbarButton)) { --_pageIndicator; }
                }

                EditorGUILayout.LabelField(new GUIContent(string.Format("{0}/{1}", _pageIndicator + 1, maxPage + 1)),
                    centeredLabelStyle, GUILayout.Width(75));

                using (new EditorGUI.DisabledScope(_pageIndicator >= maxPage))
                {
                    if (GUILayout.Button(">>", EditorStyles.toolbarButton)) { ++_pageIndicator; }
                }
            }

            EditorGUILayout.Separator();
        }

        private void SearchBar()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _filter = _searchField.OnToolbarGUI(_filter);
                if (check.changed) { FilteredResults(); }
            }
        }

        #region Helpers

        private void FilteredResults()
        {
            _filteredGameObjects.Clear();
            _filteredScriptableObjects.Clear();
            _filteredObjects.Clear();

            var hasFilter = !string.IsNullOrEmpty(_filter);
            var filter = _filter != null ? _filter.ToLower() : null;

            if (showGameObjects)
            {
                if (hasFilter)
                {
                    foreach (var instance in UniInspectEditor.GameObjectInstances)
                    {
                        var match = false;
                        if (instance.Key.name.ToLower().Contains(filter)) { match = true; }
                        else
                        {
                            if (instance.Value.Any(m => m.name.ToLower().Contains(filter))) { match = true; }
                            else
                            {
                                var monoTypes = instance.Value.Select(m => m.GetType()).ToArray();

                                if (monoTypes.Any(m => m.Name.ToLower().Contains(filter))) { match = true; }
                                else
                                {
                                    foreach (var monoType in monoTypes)
                                    {
                                        var fields = UniInspectEditor.GetMonoFields(monoType);

                                        if (fields.Any(f => f.Name.ToLower().Contains(filter))) { match = true; }
                                    }
                                }
                            }
                        }

                        if (match) { _filteredGameObjects.Add(instance.Key); }
                    }
                }
                else { _filteredGameObjects = UniInspectEditor.GameObjectInstances.Select(g => g.Key).ToList(); }
            }

            if (showScriptableObjects)
            {
                if (hasFilter)
                {
                    foreach (var instance in UniInspectEditor.ScriptableObjectInstances)
                    {
                        var pass = false;
                        if (instance.name.ToLower().Contains(filter)) { pass = true; }
                        else
                        {
                            var soType = instance.GetType();
                            if (soType.Name.ToLower().Contains(filter)) { pass = true; }
                            else
                            {
                                var fields = UniInspectEditor.GetScriptableObjectFields(soType);

                                if (fields.Any(f => f.Name.ToLower().Contains(filter))) { pass = true; }
                            }
                        }

                        if (pass) { _filteredScriptableObjects.Add(instance); }
                    }
                }
                else
                {
                    _filteredScriptableObjects = new List<ScriptableObject>(UniInspectEditor.ScriptableObjectInstances);
                }
            }

            _filteredObjects.AddRange(_filteredGameObjects.Select(go => go as Object));
            _filteredObjects.AddRange(_filteredScriptableObjects.Select(so => so as Object));
        }

        private void DrawScriptableObjectItem(Object item, bool oddLine)
        {
            var fields = UniInspectEditor.GetScriptableObjectFields(item.GetType());

            if (oddLine) { GUILayout.BeginHorizontal(line1Style); }
            else { GUILayout.BeginHorizontal(line2Style); }

            using (new GUILayout.VerticalScope())
            {
                var showObj = !_hidedObjects.Contains(item);

                DrawItemTitle(item, showObj);

                if (showObj)
                {
                    var serializedObject = new SerializedObject(item);

                    for (var i = 0; i < fields.Count; i++)
                    {
                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            var field = fields[i];
                            var sp = serializedObject.FindProperty(field.Name);

                            if (sp == null) { continue; }

                            EditorGUILayout.PropertyField(sp);

                            if (!check.changed) { continue; }

                            serializedObject.ApplyModifiedProperties();
                        }
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawGameObjectItem(Object item, List<MonoBehaviour> children, bool oddLine)
        {
            var multipleMonos = children.Count > 1;

            GUILayout.BeginHorizontal(oddLine ? line1Style : line2Style);

            using (new GUILayout.VerticalScope())
            {
                var showObj = !_hidedObjects.Contains(item);

                // Label
                DrawItemTitle(item, showObj);

                if (showObj)
                {
                    for (var i = 0; i < children.Count; i++)
                    {
                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            var instance = children[i];
                            var cursor = instance.GetType();
                            var show = !_hidedMonoBehaviours.Contains(instance);
                            var fields = UniInspectEditor.GetFields(instance);

                            var serializedObject = new SerializedObject(instance);

                            if (multipleMonos)
                            {
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                                var displayName = string.Format("{0} [{1}]", cursor.Name, show ? "-" : "+");
                                ToggleButton(displayName, show,
                                    () => _hidedMonoBehaviours.Add(instance),
                                    () => _hidedMonoBehaviours.Remove(instance),
                                    centeredLabelStyle);
                            }

                            if (show)
                            {
                                for (var j = 0; j < fields.Count; j++)
                                {
                                    var field = fields[j];
                                    var sp = serializedObject.FindProperty(field.Name);

                                    if (sp == null) { continue; }

                                    EditorGUILayout.PropertyField(sp);
                                }
                            }

                            if (multipleMonos) { EditorGUILayout.EndVertical(); }

                            if (!check.changed) { continue; }

                            serializedObject.ApplyModifiedProperties();
                        }
                    }

                    EditorGUILayout.Separator();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawItemTitle(Object item, bool showObj)
        {
            var displayName = string.Format("{0} [{1}]", item.name, showObj ? "-" : "+");
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(goIcon, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(24),
                    GUILayout.Height(24))) { SelectObject(item); }

                ToggleButton(displayName, showObj,
                    () => _hidedObjects.Add(item),
                    () => _hidedObjects.Remove(item),
                    objectLabelStyle);

                ItemSelections(item, 24);
            }
        }

        private void ToggleButton(string displayName, bool toggle, Action onAction, Action offAction, GUIStyle style)
        {
            if (!GUILayout.Button(displayName, style, GUILayout.Height(24), GUILayout.ExpandWidth(true))) { return; }

            if (toggle)
            {
                if (onAction != null) { onAction.Invoke(); }
            }
            else
            {
                if (offAction != null) { offAction.Invoke(); }
            }

            Repaint();
        }

        private static void SelectObject(Object target)
        {
            Selection.activeObject = target;
            Selection.objects = new[] {target};
            EditorGUIUtility.PingObject(target);
        }

        private void ItemSelections(Object item, float height)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(200)))
            {
                using (new GUILayout.VerticalScope(GUILayout.Height(height)))
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Select", GUILayout.Width(70))) { SelectObject(item); }

                    GUILayout.FlexibleSpace();
                }

                using (new GUILayout.VerticalScope(GUILayout.Height(height)))
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Add to selection", GUILayout.Width(100)))
                    {
                        Selection.objects = new List<Object>(Selection.objects) {item}.ToArray();
                        EditorGUIUtility.PingObject(item);
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        public static void ToggleScriptableObjects()
        {
            showScriptableObjects = !showScriptableObjects;
            Window.FilteredResults();
        }

        private static void ToggleGameObjects()
        {
            showGameObjects = !showGameObjects;
            Window.FilteredResults();
        }

        private static Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (var i = 0;
                i < pix.Length;
                i++) { pix[i] = col; }

            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        #endregion
    }
}