using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off editor helpers for cleaning up and organizing the anubhav scene
/// hierarchy as it's built incrementally from tooling. Everything here uses
/// real C# API calls so object references never need to cross the MCP
/// reflection bridge's JSON boundary.
/// </summary>
public static class SceneOrganizationUtility
{
    public static void SetPlayMode(bool playing)
    {
        EditorApplication.isPlaying = playing;
    }

    public static string GetPlayModeStatus()
    {
        return $"isPlaying={EditorApplication.isPlaying} isPaused={EditorApplication.isPaused} isCompiling={EditorApplication.isCompiling}";
    }

    public static void RebuildFeedbackHud(string cameraAnchorName, string oldTimerCanvasNameToRemove)
    {
        DestroyIfFound("FeedbackHUD_Canvas");
        DestroyIfFound("UIManager");
        FeedbackHudSetupUtility.BuildFeedbackHud(cameraAnchorName, oldTimerCanvasNameToRemove);
    }

    public static void RespawnAudience(string audienceManagerName)
    {
        GameObject go = GameObject.Find(audienceManagerName);
        if (go == null)
        {
            Debug.LogError($"SceneOrganizationUtility: '{audienceManagerName}' not found.");
            return;
        }

        AudienceManager manager = go.GetComponent<AudienceManager>();
        if (manager == null)
        {
            Debug.LogError($"SceneOrganizationUtility: '{audienceManagerName}' has no AudienceManager.");
            return;
        }

        manager.SpawnAudience();
        EditorUtility.SetDirty(manager);
    }

    public static void SetupVoicePipeline(string uiManagerName, string audienceManagerName)
    {
        GameObject existing = GameObject.Find("VoicePipeline");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject pipelineGo = new GameObject("VoicePipeline");
        AudioManager audioManager = pipelineGo.AddComponent<AudioManager>();
        HubClient hubClient = pipelineGo.AddComponent<HubClient>();
        AudioSource ttsSource = pipelineGo.AddComponent<AudioSource>();
        ttsSource.playOnAwake = false;
        ttsSource.spatialBlend = 0f;

        GameObject uiManagerGo = string.IsNullOrEmpty(uiManagerName) ? null : GameObject.Find(uiManagerName);
        GameObject audienceManagerGo = string.IsNullOrEmpty(audienceManagerName) ? null : GameObject.Find(audienceManagerName);

        SerializedObject so = new SerializedObject(hubClient);
        SetRef(so, "audioManager", audioManager);
        SetRef(so, "uiManager", uiManagerGo != null ? uiManagerGo.GetComponent<UIManager>() : null);
        SetRef(so, "audienceManager", audienceManagerGo != null ? audienceManagerGo.GetComponent<AudienceManager>() : null);
        SetRef(so, "ttsAudioSource", ttsSource);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(pipelineGo);
    }

    /// <summary>
    /// Creates (or re-wires, if it already exists) the SessionController that
    /// drives the HUD timer and provides the single entry point for ending a
    /// practice speech - nothing was calling UIManager.UpdateTimer or
    /// HubClient.EndSession before this existed.
    /// </summary>
    public static void SetupSessionController(string uiManagerName, string hubClientOwnerName)
    {
        GameObject go = GameObject.Find("SessionController");
        if (go == null)
        {
            go = new GameObject("SessionController");
        }

        SessionController controller = go.GetComponent<SessionController>();
        if (controller == null)
        {
            controller = go.AddComponent<SessionController>();
        }

        GameObject uiManagerGo = string.IsNullOrEmpty(uiManagerName) ? null : GameObject.Find(uiManagerName);
        GameObject hubClientGo = string.IsNullOrEmpty(hubClientOwnerName) ? null : GameObject.Find(hubClientOwnerName);

        SerializedObject so = new SerializedObject(controller);
        SetRef(so, "uiManager", uiManagerGo != null ? uiManagerGo.GetComponent<UIManager>() : null);
        SetRef(so, "hubClient", hubClientGo != null ? hubClientGo.GetComponent<HubClient>() : null);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    /// <summary>
    /// Groups every top-level scene object into labeled empty parents by
    /// category, for a readable Hierarchy. The XR camera rig is
    /// deliberately left at scene root, unparented - Meta's Building Block
    /// scripts (OVRManager/OVRCameraRig) are not guaranteed to behave the
    /// same when nested, and there's no benefit to moving it.
    /// </summary>
    /// <summary>
    /// Some objects named earlier in this project's history (via the MCP
    /// reflection bridge's JSON string encoding) ended up with literal
    /// double-quote characters embedded in their name, e.g. the GameObject
    /// is really named [\"Stage_Platform\"] instead of [Stage_Platform].
    /// Strips those so name-prefix matching (and just general hygiene)
    /// works correctly.
    /// </summary>
    public static void FixQuotedNames()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            FixQuotedNameRecursive(root);
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void FixQuotedNameRecursive(GameObject go)
    {
        if (go.name.Length >= 2 && go.name.StartsWith("\"") && go.name.EndsWith("\""))
        {
            go.name = go.name.Substring(1, go.name.Length - 2);
        }

        foreach (Transform child in go.transform)
        {
            FixQuotedNameRecursive(child.gameObject);
        }
    }

    public static void RemoveDuplicateGameObjectsByName(string name, int keepCount)
    {
        GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        int kept = 0;
        foreach (GameObject go in all)
        {
            if (go.name != name)
            {
                continue;
            }

            kept++;
            if (kept > keepCount)
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public static void OrganizeHierarchy()
    {
        Transform stageGroup = GetOrCreateGroup("-- Stage --");
        Transform roomGroup = GetOrCreateGroup("-- Room --");
        Transform lightingGroup = GetOrCreateGroup("-- Lighting --");
        Transform audioGroup = GetOrCreateGroup("-- Audio --");
        Transform systemsGroup = GetOrCreateGroup("-- Systems --");
        Transform propsGroup = GetOrCreateGroup("-- Props --");

        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (GameObject go in roots)
        {
            string name = go.name;

            if (name.StartsWith("-- ") || name.Contains("Camera Rig"))
            {
                continue; // group markers themselves, and the XR rig, stay put
            }

            if (name.StartsWith("Stage_"))
            {
                Reparent(go, stageGroup);
            }
            else if (name.StartsWith("Room_"))
            {
                Reparent(go, roomGroup);
            }
            else if (name == "Directional Light" || name.StartsWith("Light_"))
            {
                Reparent(go, lightingGroup);
            }
            else if (name.StartsWith("Audio_"))
            {
                Reparent(go, audioGroup);
            }
            else if (name == "AudienceManager" || name == "UIManager" || name == "VoicePipeline" || name == "PlayerAuraRing" || name == "SessionController")
            {
                Reparent(go, systemsGroup);
            }
            else if (name.EndsWith("_LODGroup"))
            {
                Reparent(go, propsGroup);
            }
            // Anything else (the group markers, the camera rig) is left alone.
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static Transform GetOrCreateGroup(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            return existing.transform;
        }

        GameObject group = new GameObject(name);
        group.transform.position = Vector3.zero;
        group.transform.rotation = Quaternion.identity;
        return group.transform;
    }

    private static void Reparent(GameObject go, Transform newParent)
    {
        if (go.transform.parent == newParent)
        {
            return;
        }

        go.transform.SetParent(newParent, true);
    }

    private static void DestroyIfFound(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null)
        {
            Object.DestroyImmediate(go);
        }
    }

    private static void SetRef(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"SceneOrganizationUtility: serialized property '{fieldName}' not found on {so.targetObject.GetType().Name}.");
            return;
        }

        prop.objectReferenceValue = value;
    }
}
