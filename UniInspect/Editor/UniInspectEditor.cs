using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UniInspect
{
    public class UniInspectEditor
    {
        public static readonly Dictionary<Type, List<FieldInfo>> MonoBehaviorRefs =
            new Dictionary<Type, List<FieldInfo>>();

        public static readonly Dictionary<Type, List<FieldInfo>> ScriptableObjectRefs =
            new Dictionary<Type, List<FieldInfo>>();

        public static readonly Dictionary<GameObject, List<MonoBehaviour>> GameObjectInstances =
            new Dictionary<GameObject, List<MonoBehaviour>>();

        public static readonly List<ScriptableObject> ScriptableObjectInstances = new List<ScriptableObject>();

        public static readonly List<string> GameObjectCollections = new List<string>();
        public static readonly List<string> ScriptableObjectCollections = new List<string>();

        private static readonly Dictionary<Type, List<FieldInfo>> _typeCaches = new Dictionary<Type, List<FieldInfo>>();

        [InitializeOnLoadMethod]
        private static void Setup()
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(t => t.GetTypes());
            var enumerable = allTypes as Type[] ?? allTypes.ToArray();

            var monoTypes = enumerable.Where(t => typeof(MonoBehaviour).IsAssignableFrom(t));
            var soTypes = enumerable.Where(t => typeof(ScriptableObject).IsAssignableFrom(t));

            // MonoBehaviors
            var targetTypes = monoTypes as Type[] ?? monoTypes.ToArray();
            foreach (var targetType in targetTypes)
            {
                var baseType = targetType.BaseType;
                if (baseType != null && baseType != typeof(MonoBehaviour))
                {
                    if (targetTypes.Contains(baseType)) { continue; }
                }

                var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if (field.IsNotSerialized) { continue; }

                    var attributes = field.GetCustomAttributes(typeof(UniInspectAttribute), false);

                    if (attributes.Length <= 0) { continue; }

                    if (!MonoBehaviorRefs.ContainsKey(targetType))
                    {
                        MonoBehaviorRefs.Add(targetType, new List<FieldInfo>(new[] {field}));
                    }
                    else { MonoBehaviorRefs[targetType].Add(field); }

//                    Debug.LogFormat("[Mono]UniInspect Added {0} on {1}.", field, targetType);
                }
            }

            // ScriptableObjects 
            targetTypes = soTypes as Type[] ?? soTypes.ToArray();
            foreach (var targetType in targetTypes)
            {
                var baseType = targetType.BaseType;
                if (baseType != null && baseType != typeof(ScriptableObject))
                {
                    if (targetTypes.Contains(baseType)) { continue; }
                }

                var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if (field.IsNotSerialized) { continue; }

                    var attributes = field.GetCustomAttributes(typeof(UniInspectAttribute), false);

                    if (attributes.Length <= 0) { continue; }

                    if (!ScriptableObjectRefs.ContainsKey(targetType))
                    {
                        ScriptableObjectRefs.Add(targetType, new List<FieldInfo>(new[] {field}));
                    }
                    else { ScriptableObjectRefs[targetType].Add(field); }

//                    Debug.LogFormat("[SO]UniInspect Added {0} on {1}.", field, targetType);
                }
            }

            CollectObjects();
        }

        public static void CollectObjects()
        {
            GameObjectCollections.Clear();
            ScriptableObjectCollections.Clear();

            GameObjectInstances.Clear();
            ScriptableObjectInstances.Clear();

            var allGos = new List<GameObject>();
            var allSos = new List<ScriptableObject>();
//            var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
//            var allSos = Resources.FindObjectsOfTypeAll<ScriptableObject>();

            var goAssets = AssetDatabase.FindAssets("t:GameObject");
            var soAssets = AssetDatabase.FindAssets("t:ScriptableObject");
            for (var i = 0; i < goAssets.Length; i++)
            {
                var goAsset = goAssets[i];
                var path = AssetDatabase.GUIDToAssetPath(goAsset);
                allGos.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            }

            for (var i = 0; i < soAssets.Length; i++)
            {
                var soAsset = soAssets[i];
                var path = AssetDatabase.GUIDToAssetPath(soAsset);
                allSos.Add(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path));
            }

            var monoTypes = MonoBehaviorRefs.Select(x => x.Key);
            var soTypes = ScriptableObjectRefs.Select(x => x.Key);

            var monoTypeEnumerable = monoTypes as Type[] ?? monoTypes.ToArray();
            var soTypeEnumerable = soTypes as Type[] ?? soTypes.ToArray();

            for (var i = 0; i < allGos.Count; i++)
            {
                var curGo = allGos[i];

                if (!string.IsNullOrEmpty(curGo.scene.name) || !EditorUtility.IsPersistent(curGo)) { continue; }

                foreach (var monoType in monoTypeEnumerable)
                {
                    var comps = curGo.GetComponentsInChildren(monoType);

                    for (var j = 0; j < comps.Length; j++)
                    {
                        var comp = comps[j] as MonoBehaviour;

                        if (comp == null) { continue; }

                        if (!GameObjectInstances.ContainsKey(curGo))
                        {
                            GameObjectCollections.Add(AssetDatabase.GetAssetPath(curGo));
                            GameObjectInstances.Add(curGo, new List<MonoBehaviour>(new[] {comp}));
                        }
                        else { GameObjectInstances[curGo].Add(comp); }
                    }
                }
            }

            for (var i = 0; i < allSos.Count; i++)
            {
                var curSo = allSos[i];
                foreach (var soType in soTypeEnumerable)
                {
                    if (!soType.IsInstanceOfType(curSo)) { continue; }

                    ScriptableObjectInstances.Add(curSo);
                    ScriptableObjectCollections.Add(AssetDatabase.GetAssetPath(curSo));
                }
            }
        }

        public static List<FieldInfo> GetFields(MonoBehaviour mono)
        {
            return GetMonoFields(mono.GetType());
        }

        public static List<FieldInfo> GetFields(ScriptableObject so)
        {
            return GetScriptableObjectFields(so.GetType());
        }

        public static List<FieldInfo> GetMonoFields(Type type)
        {
            if (type == null) { return null; }

            List<FieldInfo> fields;

            if (!_typeCaches.TryGetValue(type, out fields))
            {
                var cursor = type;
                while (!MonoBehaviorRefs.ContainsKey(cursor))
                {
                    if (cursor.BaseType == null) { return null; }

                    cursor = cursor.BaseType;
                }

                fields = MonoBehaviorRefs[cursor];
                _typeCaches.Add(type, fields);
            }

            return fields;
        }

        public static List<FieldInfo> GetScriptableObjectFields(Type type)
        {
            if (type == null) { return null; }

            List<FieldInfo> fields;

            if (!_typeCaches.TryGetValue(type, out fields))
            {
                var cursor = type;
                while (!ScriptableObjectRefs.ContainsKey(cursor))
                {
                    if (cursor.BaseType == null) { return null; }

                    cursor = cursor.BaseType;
                }

                fields = ScriptableObjectRefs[cursor];
                _typeCaches.Add(type, fields);
            }

            return fields;
        }
    }
}