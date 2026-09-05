using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off editor helpers for swapping the stage's primitive placeholders
/// (podium boxes, seat cubes, backdrop wall) for the real downloaded
/// meshes (podium, cinema seats, curtain, stage light).
/// </summary>
public static class PropWiringUtility
{
    public static string GetPrefabBounds(string assetPath)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
        {
            return $"ERROR: no asset at {assetPath}";
        }

        GameObject temp = Object.Instantiate(asset);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one;

        Bounds bounds = CalculateBounds(temp);
        Object.DestroyImmediate(temp);

        return $"size=({bounds.size.x:0.###}, {bounds.size.y:0.###}, {bounds.size.z:0.###}) center=({bounds.center.x:0.###}, {bounds.center.y:0.###}, {bounds.center.z:0.###})";
    }

    private static Bounds CalculateBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(go.transform.position, Vector3.zero);
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        return b;
    }

    /// <summary>
    /// Instantiates assetPath, uniformly rescales it so its tallest/relevant
    /// axis matches targetSize, and places it at targetPosition +
    /// targetEulerY rotation. Returns the new instance's name for follow-up
    /// tweaks. scaleAxis: 0=x,1=y,2=z chooses which local bounds axis to
    /// match against targetSize before positioning.
    /// </summary>
    public static string PlaceScaledPrefab(
        string assetPath, string instanceName,
        float targetSize, int scaleAxis,
        float posX, float posY, float posZ,
        float eulerY, string parentName)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
        {
            Debug.LogError($"PropWiringUtility: no asset at {assetPath}");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (instance == null)
        {
            instance = Object.Instantiate(asset);
        }
        instance.name = instanceName;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        Bounds localBounds = CalculateBounds(instance);
        float rawSize = scaleAxis == 0 ? localBounds.size.x : (scaleAxis == 1 ? localBounds.size.y : localBounds.size.z);
        float scale = rawSize > 0.0001f ? targetSize / rawSize : 1f;
        instance.transform.localScale = Vector3.one * scale;

        // Re-measure after scaling to find how far the mesh's own pivot sits
        // below/above its bounds min, so we can place the pivot such that
        // the object's bottom rests exactly at posY.
        Bounds scaledBounds = CalculateBounds(instance);
        float pivotToBottom = instance.transform.position.y - scaledBounds.min.y;

        instance.transform.SetPositionAndRotation(
            new Vector3(posX, posY + pivotToBottom, posZ),
            Quaternion.Euler(0f, eulerY, 0f));

        if (!string.IsNullOrEmpty(parentName))
        {
            GameObject parent = GameObject.Find(parentName);
            if (parent != null)
            {
                instance.transform.SetParent(parent.transform, true);
            }
        }

        EditorUtility.SetDirty(instance);
        return instance.name;
    }

    public static void RemoveAllByPrefix(string prefix)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        List<GameObject> toRemove = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            CollectByPrefixRecursive(root, prefix, toRemove);
        }
        foreach (GameObject go in toRemove)
        {
            Object.DestroyImmediate(go);
        }
    }

    private static void CollectByPrefixRecursive(GameObject go, string prefix, List<GameObject> results)
    {
        if (go.name.StartsWith(prefix))
        {
            results.Add(go);
            return; // don't recurse into something we're about to destroy
        }
        foreach (Transform child in go.transform)
        {
            CollectByPrefixRecursive(child.gameObject, prefix, results);
        }
    }

    /// <summary>
    /// Places a rows x columns grid of assetPath instances using the exact
    /// same layout formula as AudienceManager.SpawnAudience, so seats align
    /// precisely under each spawned audience member.
    /// </summary>
    public static void PlaceSeatGrid(
        string assetPath, string namePrefix,
        int rows, int columns, float rowSpacing, float columnSpacing,
        float originX, float originZ,
        float targetSize, int scaleAxis, float eulerY, string parentName)
    {
        float gridWidth = (columns - 1) * columnSpacing;
        GameObject parent = string.IsNullOrEmpty(parentName) ? null : GameObject.Find(parentName);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                float x = originX - gridWidth * 0.5f + col * columnSpacing;
                float z = originZ - row * rowSpacing;
                string name = $"{namePrefix}{(row * columns + col + 1):00}";

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    Debug.LogError($"PropWiringUtility: no asset at {assetPath}");
                    return;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.name = name;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                Bounds localBounds = CalculateBounds(instance);
                float rawSize = scaleAxis == 0 ? localBounds.size.x : (scaleAxis == 1 ? localBounds.size.y : localBounds.size.z);
                float scale = rawSize > 0.0001f ? targetSize / rawSize : 1f;
                instance.transform.localScale = Vector3.one * scale;

                Bounds scaledBounds = CalculateBounds(instance);
                float pivotToBottom = -scaledBounds.min.y;

                instance.transform.SetPositionAndRotation(
                    new Vector3(x, pivotToBottom, z),
                    Quaternion.Euler(0f, eulerY, 0f));

                if (parent != null)
                {
                    instance.transform.SetParent(parent.transform, true);
                }

                EditorUtility.SetDirty(instance);
            }
        }
    }

    public static void RemoveByName(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null)
        {
            Object.DestroyImmediate(go);
        }
    }

    public static void FrameObjectInSceneView(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            Debug.LogError($"PropWiringUtility: '{name}' not found.");
            return;
        }

        Selection.activeGameObject = go;
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.FrameSelected();
        }
    }

    public static void SetSceneViewTopDown(float pivotX, float pivotY, float pivotZ, float size)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            Debug.LogError("PropWiringUtility: no active SceneView.");
            return;
        }

        view.pivot = new Vector3(pivotX, pivotY, pivotZ);
        view.rotation = Quaternion.Euler(90f, 0f, 0f);
        view.size = size;
        view.orthographic = true;
        view.Repaint();
    }

    public static void SaveScene()
    {
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }
}
