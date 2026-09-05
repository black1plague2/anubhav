using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;

/// <summary>
/// One-off editor helpers used while wiring up the Audience system from
/// tooling that can't pass Unity object references directly (only strings/
/// primitives). Everything here resolves its own references internally via
/// AssetDatabase/GameObject.Find so no object reference needs to cross that
/// boundary.
/// </summary>
public static class AudienceSetupUtility
{
    public static void AssignAnimatorControllerToGameObject(string gameObjectName, string controllerAssetPath)
    {
        GameObject go = GameObject.Find(gameObjectName);
        if (go == null)
        {
            Debug.LogError($"AudienceSetupUtility: GameObject '{gameObjectName}' not found.");
            return;
        }

        Animator animator = go.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError($"AudienceSetupUtility: '{gameObjectName}' has no Animator component.");
            return;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerAssetPath);
        if (controller == null)
        {
            Debug.LogError($"AudienceSetupUtility: No AnimatorController found at '{controllerAssetPath}'.");
            return;
        }

        animator.runtimeAnimatorController = controller;
        EditorUtility.SetDirty(animator);
    }

    public static void SaveGameObjectAsPrefab(string gameObjectName, string prefabSavePath)
    {
        GameObject go = GameObject.Find(gameObjectName);
        if (go == null)
        {
            Debug.LogError($"AudienceSetupUtility: GameObject '{gameObjectName}' not found.");
            return;
        }

        PrefabUtility.SaveAsPrefabAsset(go, prefabSavePath);
        AssetDatabase.SaveAssets();
    }

    public static void DestroyGameObjectByName(string gameObjectName)
    {
        GameObject go = GameObject.Find(gameObjectName);
        if (go == null)
        {
            Debug.LogWarning($"AudienceSetupUtility: GameObject '{gameObjectName}' not found (already destroyed?).");
            return;
        }

        Object.DestroyImmediate(go);
    }

    public static string GetAnimatorStateName(string gameObjectName, int layerIndex)
    {
        GameObject go = GameObject.Find(gameObjectName);
        if (go == null)
        {
            return "ERROR: GameObject not found";
        }

        Animator animator = go.GetComponent<Animator>();
        if (animator == null)
        {
            return "ERROR: No Animator";
        }

        var info = animator.GetCurrentAnimatorStateInfo(layerIndex);
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            return $"nameHash={info.shortNameHash}";
        }

        foreach (var state in controller.layers[layerIndex].stateMachine.states)
        {
            if (Animator.StringToHash(state.state.name) == info.shortNameHash)
            {
                return state.state.name;
            }
        }

        return $"UNKNOWN (nameHash={info.shortNameHash})";
    }

    public static void AssignPrefabToAudienceManager(string managerGameObjectName, string prefabAssetPath)
    {
        GameObject managerGo = GameObject.Find(managerGameObjectName);
        if (managerGo == null)
        {
            Debug.LogError($"AudienceSetupUtility: GameObject '{managerGameObjectName}' not found.");
            return;
        }

        AudienceManager manager = managerGo.GetComponent<AudienceManager>();
        if (manager == null)
        {
            Debug.LogError($"AudienceSetupUtility: '{managerGameObjectName}' has no AudienceManager component.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (prefab == null)
        {
            Debug.LogError($"AudienceSetupUtility: No prefab found at '{prefabAssetPath}'.");
            return;
        }

        SerializedObject so = new SerializedObject(manager);
        SerializedProperty prop = so.FindProperty("audienceMemberPrefab");
        if (prop == null)
        {
            Debug.LogError("AudienceSetupUtility: Could not find serialized property 'audienceMemberPrefab' on AudienceManager.");
            return;
        }

        prop.objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
    }

    /// <summary>Assigns multiple prefabs (comma-separated asset paths) to AudienceManager's prefab array field.</summary>
    public static void AssignPrefabArrayToAudienceManager(string managerGameObjectName, string commaSeparatedPrefabPaths)
    {
        GameObject managerGo = GameObject.Find(managerGameObjectName);
        if (managerGo == null)
        {
            Debug.LogError($"AudienceSetupUtility: GameObject '{managerGameObjectName}' not found.");
            return;
        }

        AudienceManager manager = managerGo.GetComponent<AudienceManager>();
        if (manager == null)
        {
            Debug.LogError($"AudienceSetupUtility: '{managerGameObjectName}' has no AudienceManager component.");
            return;
        }

        string[] paths = commaSeparatedPrefabPaths.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        GameObject[] prefabs = new GameObject[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (prefabs[i] == null)
            {
                Debug.LogError($"AudienceSetupUtility: no prefab found at '{paths[i]}'.");
                return;
            }
        }

        SerializedObject so = new SerializedObject(manager);
        SerializedProperty arrayProp = so.FindProperty("audienceMemberPrefabs");
        if (arrayProp == null)
        {
            Debug.LogError("AudienceSetupUtility: Could not find serialized property 'audienceMemberPrefabs' on AudienceManager.");
            return;
        }

        arrayProp.arraySize = prefabs.Length;
        for (int i = 0; i < prefabs.Length; i++)
        {
            arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
    }
}
