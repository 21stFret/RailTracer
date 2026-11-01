// RailTracer - Train Car Builder (Editor tool)
// Put this file in any folder named "Editor" (e.g. Assets/RailTracer/Editor/TrainCarBuilder.cs).
// Open it from the menu:  Tools > RailTracer > Train Car Builder
//
// Builds a train car from the modular FBX kit (railtracer_train_kit.py) using a layout string.
// Wall tokens (read left to right, from the front of the car to the back):
//   S = Wall_1m_Solid        P = Wall_1m_Pilaster     w = Wall_1m_WindowSmall
//   W = Wall_2m_Window       D = Wall_2m_Door         | = Partition_Door (side A string only, 0 m long)
// Spaces are ignored. Both sides must add up to the same length.
// Roofs follow side A automatically: W/D -> Roof_2m_Lamp, P -> Roof_1m_Rib, S/w -> Roof_1m.
// The car runs along +Z from the builder root; it is (wall length + 1 m) long because of the end caps.
// Example: "S W P W P W P W S" = 13 m of walls -> the original 14 m coach.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TrainCarBuilder : EditorWindow
{
    enum EndType { Door, Solid }

    string folder = "Assets/RailTracer/Train Assets";
    string carName = "TrainCar_Coach";
    string sideA = "S W P W P W P W S";
    string sideB = "";
    EndType frontEnd = EndType.Door;
    EndType backEnd = EndType.Door;
    bool runner = true;
    bool flipEndCaps = false;
    bool flipSides = false;

    static readonly Dictionary<char, (string name, int len)> Walls = new Dictionary<char, (string, int)>
    {
        { 'S', ("Wall_1m_Solid", 1) }, { 'P', ("Wall_1m_Pilaster", 1) }, { 'w', ("Wall_1m_WindowSmall", 1) },
        { 'W', ("Wall_2m_Window", 2) }, { 'D', ("Wall_2m_Door", 2) },
    };
    static readonly Dictionary<char, string> Roofs = new Dictionary<char, string>
    {
        { 'S', "Roof_1m" }, { 'P', "Roof_1m_Rib" }, { 'w', "Roof_1m" }, { 'W', "Roof_2m_Lamp" }, { 'D', "Roof_2m_Lamp" },
    };

    [MenuItem("Tools/RailTracer/Train Car Builder")]
    static void Open() => GetWindow<TrainCarBuilder>("Train Car Builder");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
        folder = EditorGUILayout.TextField("FBX folder", folder);
        carName = EditorGUILayout.TextField("Car name", carName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout  (S P w = 1 m, W D = 2 m, | = partition)", EditorStyles.boldLabel);
        sideA = EditorGUILayout.TextField("Side A", sideA);
        sideB = EditorGUILayout.TextField(new GUIContent("Side B", "Empty = same as side A"), sideB);
        frontEnd = (EndType)EditorGUILayout.EnumPopup("Front end", frontEnd);
        backEnd = (EndType)EditorGUILayout.EnumPopup("Back end", backEnd);
        runner = EditorGUILayout.Toggle("Velvet aisle runner", runner);

        int la = Length(sideA), lb = Length(string.IsNullOrWhiteSpace(sideB) ? sideA : sideB);
        string msg = la == lb ? $"Walls {la} m  ->  car {la + 1} m long" : $"Side A is {la} m but side B is {lb} m";
        EditorGUILayout.HelpBox(msg, la == lb ? MessageType.Info : MessageType.Error);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("If something comes out backwards", EditorStyles.boldLabel);
        flipEndCaps = EditorGUILayout.Toggle("Flip end caps", flipEndCaps);
        flipSides = EditorGUILayout.Toggle("Swap wall sides", flipSides);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(la != lb || la == 0))
        {
            if (GUILayout.Button("Build car", GUILayout.Height(30)))
                Build(la);
        }
    }

    static int Length(string layout)
    {
        int n = 0;
        foreach (char c in layout)
            if (Walls.TryGetValue(c, out var w)) n += w.len;
        return n;
    }

    void Build(int wallLength)
    {
        var root = new GameObject(carName);
        Undo.RegisterCreatedObjectUndo(root, "Build train car");
        if (SceneView.lastActiveSceneView != null)
            root.transform.position = SceneView.lastActiveSceneView.pivot;

        // Blender (x, y, z) arrives in Unity as (-x, z, -y): the kit's +Y end faces Unity -Z.
        // The car is laid out along Unity +Z starting at the root.
        float capFront = flipEndCaps ? 180f : 0f;
        float capBack = flipEndCaps ? 0f : 180f;
        Place(EndName(frontEnd), 0.25f, capFront, root.transform);
        Place(EndName(backEnd), wallLength + 0.75f, capBack, root.transform);

        float rotA = flipSides ? 180f : 0f, rotB = flipSides ? 0f : 180f;
        PlaceSide(sideA, rotA, true, root.transform);
        PlaceSide(string.IsNullOrWhiteSpace(sideB) ? sideA : sideB, rotB, false, root.transform);

        for (int k = 0; k < wallLength; k++)
            Place(runner ? "Floor_1m" : "Floor_1m_Plain", 0.5f + k + 0.5f, 0f, root.transform);

        Selection.activeGameObject = root;
        Debug.Log($"[TrainCarBuilder] Built {carName}: {wallLength + 1} m, side A \"{sideA}\"");
    }

    void PlaceSide(string layout, float rot, bool withRoofAndPartitions, Transform parent)
    {
        float s = 0.5f;                                    // walls start after the front end cap
        foreach (char c in layout)
        {
            if (c == '|')
            {
                if (withRoofAndPartitions) Place("Partition_Door", s, 0f, parent);
                continue;
            }
            if (!Walls.TryGetValue(c, out var w)) continue;   // spaces / unknown characters
            Place(w.name, s + w.len / 2f, rot, parent);
            if (withRoofAndPartitions) Place(Roofs[c], s + w.len / 2f, 0f, parent);
            s += w.len;
        }
    }

    static string EndName(EndType t) => t == EndType.Door ? "EndCap_Door" : "EndCap_Solid";

    GameObject Place(string module, float z, float rotY, Transform parent)
    {
        string path = folder.TrimEnd('/') + "/" + module + ".fbx";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[TrainCarBuilder] Missing module: {path}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        return go;
    }
}
