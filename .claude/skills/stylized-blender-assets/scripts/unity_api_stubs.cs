// Minimal API stand-ins so the editor scripts can be type-checked outside Unity.
using System;
using System.Collections.Generic;
namespace UnityEngine {
  public class Object { public string name; public static void DestroyImmediate(Object o, bool b) {} }
  public class Component : Object { public GameObject gameObject; public T GetComponent<T>() where T : class => null; }
  public class Transform : Component, System.Collections.IEnumerable {
    public Vector3 localPosition, position; public Quaternion localRotation;
    public System.Collections.IEnumerator GetEnumerator() => null; }
  public class GameObject : Object { public GameObject(string n) {} public Transform transform; public bool isStatic;
    public T AddComponent<T>() where T : Component, new() => new T(); public T GetComponent<T>() where T : class => null;
    public T[] GetComponentsInChildren<T>(bool b) => new T[0]; }
  public class Mesh : Object {}
  public class Shader : Object { public static Shader Find(string n) => null; }
  public class Texture : Object {} public class Texture2D : Texture {}
  public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
  public struct Vector2 { public Vector2(float x, float y) {} }
  public class Material : Object { public Material(Shader s) {} public bool HasProperty(string p) => true; public Shader shader;
    public void SetColor(string p, Color c) {} public void SetFloat(string p, float f) {}
    public void SetTexture(string p, Texture t) {} public void SetTextureScale(string p, Vector2 v) {}
    public void EnableKeyword(string k) {} public void DisableKeyword(string k) {} }
  public struct Color { public Color(float r, float g, float b, float a) {} public Color gamma => this; public static Color white => new Color(1, 1, 1, 1); }
  public static class Mathf { public static float Clamp01(float f) => f; }
  public static class Application { public static string dataPath = "/tmp/RailTracer/Assets"; }
  public static class JsonUtility { public static T FromJson<T>(string j) => default(T); }
  public class MeshFilter : Component { public Mesh sharedMesh; }
  public class MeshCollider : Component { public Mesh sharedMesh; }
  public class Renderer : Component { public Material[] sharedMaterials; }
  public class MeshRenderer : Renderer {}
  public struct Vector3 { public Vector3(float x, float y, float z) {} }
  public struct Quaternion { public static Quaternion Euler(float x, float y, float z) => new Quaternion(); }
  public class GUIContent { public GUIContent(string a, string b) {} }
  public static class Debug { public static void Log(object o) {} public static void LogError(object o) {} public static void LogWarning(object o) {} }
  public class GUILayoutOption {}
  public static class GUILayout { public static bool Button(string s, params GUILayoutOption[] o) => false; public static GUILayoutOption Height(float h) => null; }
  public class ScriptableObject : Object {}
}
namespace UnityEngine.Rendering {
  public class RenderPipelineAsset : UnityEngine.ScriptableObject {}
  public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; }
}
namespace UnityEditor {
  using UnityEngine;
  public class MenuItem : Attribute { public MenuItem(string s) {} }
  public class InitializeOnLoadAttribute : Attribute {}
  public static class EditorPrefs { public static bool GetBool(string k, bool d) => d; public static void SetBool(string k, bool v) {}
    public static string GetString(string k, string d) => d; public static void SetString(string k, string v) {} public static void DeleteKey(string k) {} }
  public static class SessionState { public static bool GetBool(string k, bool d) => d; public static void SetBool(string k, bool v) {} }
  public static class EditorGUIUtility { public static void PingObject(Object o) {} }
  public class EditorWindow : ScriptableObject { public static T GetWindow<T>(string t) where T : EditorWindow => null; }
  public enum MessageType { None, Info, Warning, Error }
  public class GUIStyle {} public static class EditorStyles { public static GUIStyle boldLabel; }
  public static class EditorGUILayout {
    public static string TextField(string l, string v) => v; public static string TextField(GUIContent l, string v) => v;
    public static Enum EnumPopup(string l, Enum e) => e; public static bool Toggle(string l, bool v) => v;
    public static void LabelField(string l, GUIStyle s) {} public static void LabelField(string l, string v) {} public static void HelpBox(string m, MessageType t) {} public static void Space() {} }
  public static class EditorGUI { public class DisabledScope : IDisposable { public DisabledScope(bool b) {} public void Dispose() {} } }
  public static class Undo { public static void RegisterCreatedObjectUndo(Object o, string n) {} }
  public class SceneView { public static SceneView lastActiveSceneView; public Vector3 pivot; }
  public static class Selection { public static GameObject activeGameObject; }
  public enum ImportAssetOptions { Default, ForceUpdate }
  public static class AssetDatabase { public static T LoadAssetAtPath<T>(string p) where T : Object => null;
    public static string[] FindAssets(string f) => new string[0]; public static string[] FindAssets(string f, string[] dirs) => new string[0];
    public static string GUIDToAssetPath(string g) => g; public static bool IsValidFolder(string p) => true;
    public static string CreateFolder(string a, string b) => ""; public static void CreateAsset(Object o, string p) {}
    public static void SaveAssets() {} public static void Refresh() {} public static void ImportAsset(string p, ImportAssetOptions o) {}
    public static void StartAssetEditing() {} public static void StopAssetEditing() {} }
  public static class EditorApplication { public delegate void CallbackFunction(); public static CallbackFunction delayCall, update;
    public static double timeSinceStartup; public static bool isCompiling, isUpdating, isPlayingOrWillChangePlaymode; }
  public static class EditorUtility { public static void SetDirty(Object o) {} public static bool DisplayDialog(string a, string b, string c) => true;
    public static int DisplayDialogComplex(string t, string m, string a, string b, string c) => 0;
    public static bool DisplayCancelableProgressBar(string t, string i, float p) => false; public static void ClearProgressBar() {}
    public static string OpenFilePanel(string t, string d, string e) => ""; }
  public static class PrefabUtility { public static Object InstantiatePrefab(Object o, Transform p) => null; }
  public class AssetImportContext { public void DependsOnSourceAsset(string p) {} }
  public class AssetImporter : Object { public static AssetImporter GetAtPath(string p) => null; public void SaveAndReimport() {} }
  public enum TextureImporterType { Default, NormalMap, Sprite }
  public class TextureImporter : AssetImporter { public TextureImporterType textureType; public bool sRGBTexture, mipmapEnabled;
    public TextureWrapMode wrapMode; }
  public class AssetPostprocessor { public string assetPath; public AssetImportContext context; }
}
