using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// AudioManager をシーンに置き、Assets/Audio 以下の全クリップを差し込む。
    /// Title と Campus の両方に置く（DontDestroyOnLoad で 2 つ目は自分を消す）。
    /// </summary>
    public static class AudioFactory
    {
        public const string AudioFolder = "Assets/Audio";

        public static void Place(Transform root)
        {
            var go = new GameObject("AudioManager");
            go.transform.SetParent(root, false);
            AudioManager manager = go.AddComponent<AudioManager>();

            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var serialized = new SerializedObject(manager);
            SerializedProperty property = serialized.FindProperty("_clips");
            property.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorPaths.Report("AudioManager にクリップを " + clips.Count + " 本差し込みました。");
        }
    }
}
