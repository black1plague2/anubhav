using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor automation for turning the raw Mixamo per-character-per-pose FBX
/// exports into: one Humanoid avatar per character, extracted animation
/// clips, a shared AnimatorController with real motions, and per-character
/// audience prefabs.
/// </summary>
public static class AudienceCharacterSetupUtility
{
    public static string InspectSubAssets(string assetPath)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<string> lines = new List<string>();
        foreach (Object o in all)
        {
            if (o == null) continue;
            lines.Add($"{o.GetType().Name}: '{o.name}'");
        }
        return string.Join(" | ", lines);
    }

    public static void ConfigureHumanoidRig(string fbxPath, string copyAvatarFromPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: no ModelImporter at {fbxPath}");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;

        if (string.IsNullOrEmpty(copyAvatarFromPath))
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
        else
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(copyAvatarFromPath);
            if (importer.sourceAvatar == null)
            {
                Debug.LogError($"AudienceCharacterSetupUtility: no Avatar found at {copyAvatarFromPath}");
            }
        }

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    public static string GetAvatarStatus(string fbxPath)
    {
        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(fbxPath);
        if (avatar == null)
        {
            return "NO AVATAR";
        }
        return avatar.isValid ? (avatar.isHuman ? "VALID HUMAN AVATAR" : "VALID BUT NOT HUMAN") : "INVALID AVATAR";
    }

    /// <summary>
    /// Mixamo animation-only exports ("without skin") have no diffuse
    /// texture at all - this tints every material sub-asset in the FBX a
    /// flat color so characters aren't uniform grey. Real textures need a
    /// re-download from Mixamo with the "Skin" export option checked.
    /// </summary>
    public static void TintFbxMaterials(string fbxPath, float r, float g, float b)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        bool any = false;
        foreach (Object o in all)
        {
            if (o is Material mat)
            {
                mat.color = new Color(r, g, b, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(r, g, b, 1f));
                EditorUtility.SetDirty(mat);
                any = true;
            }
        }
        if (!any)
        {
            Debug.LogWarning($"AudienceCharacterSetupUtility: no materials found in {fbxPath}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Role-based re-tint using each material's own name as a hint, since
    /// material structure varies per character export: some have one
    /// combined body+clothing material, others split skin/top/bottom/shoes/
    /// hair/eyelashes out separately. Falls back to skinColor for anything
    /// unrecognized (usually the main "body"/"Bodymat" material).
    /// </summary>
    public static void TintFbxMaterialsByRole(
        string fbxPath,
        Color skinColor, Color clothingColor, Color pantsColor,
        Color shoesColor, Color hairColor, Color eyelashColor)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        int tinted = 0;

        foreach (Object o in all)
        {
            if (o is not Material mat) continue;

            string n = mat.name.ToLowerInvariant();
            Color chosen;

            if (n.Contains("eyelash"))
            {
                chosen = eyelashColor;
            }
            else if (n.Contains("hair"))
            {
                chosen = hairColor;
            }
            else if (n.Contains("shoe"))
            {
                chosen = shoesColor;
            }
            else if (n.Contains("bottom") || n.Contains("pant"))
            {
                chosen = pantsColor;
            }
            else if (n.Contains("top") || n.Contains("shirt"))
            {
                chosen = clothingColor;
            }
            else if (n.EndsWith("1")) // e.g. "Ch06_body1" - treat as the clothing layer alongside a plain "Ch06_body" skin material
            {
                chosen = clothingColor;
            }
            else
            {
                chosen = skinColor; // plain "body"/"Bodymat" etc - skin (and, if this is the ONLY material, clothing too - unavoidable for single-material characters)
            }

            mat.color = chosen;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", chosen);
            EditorUtility.SetDirty(mat);
            tinted++;
        }

        if (tinted == 0)
        {
            Debug.LogWarning($"AudienceCharacterSetupUtility: no materials found in {fbxPath}");
        }
        AssetDatabase.SaveAssets();
    }

    public static string TryExtractEmbeddedTextures(string fbxPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            return "ERROR: no ModelImporter";
        }

        string destDir = System.IO.Path.GetDirectoryName(fbxPath) + "/ExtractedTextures";
        System.IO.Directory.CreateDirectory(destDir);
        bool result = importer.ExtractTextures(destDir);
        return result ? $"EXTRACTED something to {destDir}" : "NOTHING TO EXTRACT (no embedded texture data in this FBX)";
    }

    /// <summary>Assigns a diffuse texture (by asset path) as the main texture of a named material sub-asset inside an FBX.</summary>
    public static void AssignTextureToMaterial(string fbxPath, string materialName, string texturePath)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        Material target = null;
        foreach (Object o in all)
        {
            if (o is Material mat && mat.name == materialName)
            {
                target = mat;
                break;
            }
        }

        if (target == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: material '{materialName}' not found in {fbxPath}");
            return;
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (tex == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: no texture at {texturePath}");
            return;
        }

        // Reset to white so the texture's own colors show through undistorted by our earlier flat tint.
        target.color = Color.white;
        if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", Color.white);
        if (target.HasProperty("_BaseMap")) target.SetTexture("_BaseMap", tex);
        if (target.HasProperty("_MainTex")) target.SetTexture("_MainTex", tex);

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Extracts an FBX's currently-embedded (auto-generated) materials to
    /// standalone .mat assets in destinationFolder and remaps the model to
    /// use them. This MUST be called after tinting/texturing an FBX's
    /// embedded materials, or Unity can silently regenerate (and wipe) them
    /// on the next reimport/domain reload - embedded FBX materials are not
    /// guaranteed to persist edits otherwise.
    /// </summary>
    public static string ExtractMaterialsToFolder(string fbxPath, string destinationFolder)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            return "ERROR: no ModelImporter";
        }

        System.IO.Directory.CreateDirectory(destinationFolder);

        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        int count = 0;
        foreach (Object o in all)
        {
            if (o is not Material embeddedMat) continue;

            // Duplicate the embedded material (preserving whatever color/
            // texture we've already set on it) into a standalone asset,
            // then remap the model to use that standalone copy instead of
            // regenerating this material fresh on every reimport.
            Material standalone = new Material(embeddedMat);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{destinationFolder}/{embeddedMat.name}.mat");
            AssetDatabase.CreateAsset(standalone, path);

            var identifier = new AssetImporter.SourceAssetIdentifier(embeddedMat);
            importer.AddRemap(identifier, standalone);
            count++;
        }

        importer.SaveAndReimport();
        AssetDatabase.SaveAssets();

        return count > 0
            ? $"Extracted and remapped {count} material(s) to {destinationFolder}"
            : "No materials found to extract";
    }

    public static string InspectMaterialAsset(string materialPath)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null)
        {
            return "ERROR: material not found";
        }

        Shader shader = mat.shader;
        Texture mainTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : (mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null);
        return $"shader='{(shader != null ? shader.name : "NULL")}' shaderSupported={(shader != null && shader.isSupported)} mainTex={(mainTex != null ? mainTex.name : "NULL")} color={mat.color}";
    }

    public static string InspectMaterials(string fbxPath)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        var lines = new List<string>();
        foreach (Object o in all)
        {
            if (o is Material mat)
            {
                Texture mainTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                if (mainTex == null && mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");
                lines.Add($"'{mat.name}' shader={mat.shader.name} mainTex={(mainTex != null ? mainTex.name : "NULL")}");
            }
        }
        return string.Join(" | ", lines);
    }

    private static AnimationClip ExtractRealClip(string fbxPath)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (Object o in all)
        {
            if (o is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }

    /// <summary>
    /// Assigns clips extracted from idle/clap/bored/talking/disapproval FBX
    /// files directly onto the given AnimatorController's AnyState
    /// transition target states (matched by state name). This character's
    /// clips become the controller's "default" motions - other characters
    /// get an AnimatorOverrideController layered on top instead of
    /// duplicating the whole state machine.
    /// </summary>
    public static void AssignBaseControllerMotions(
        string controllerPath,
        string idleFbx, string clapFbx, string boredFbx, string talkingFbx, string disapprovalFbx)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: no AnimatorController at {controllerPath}");
            return;
        }

        var clipsByState = new Dictionary<string, AnimationClip>
        {
            ["Neutral"] = ExtractRealClip(idleFbx),
            ["Clapping"] = ExtractRealClip(clapFbx),
            ["Bored"] = ExtractRealClip(boredFbx),
            ["Engaged"] = ExtractRealClip(talkingFbx),
            ["Sympathetic"] = ExtractRealClip(talkingFbx), // no dedicated pose; reuse Talking
            ["Distracted"] = ExtractRealClip(disapprovalFbx),
        };

        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        for (int i = 0; i < states.Length; i++)
        {
            string stateName = states[i].state.name;
            if (clipsByState.TryGetValue(stateName, out AnimationClip clip) && clip != null)
            {
                states[i].state.motion = clip;
            }
            else
            {
                Debug.LogWarning($"AudienceCharacterSetupUtility: no clip resolved for state '{stateName}'.");
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Builds an AnimatorOverrideController for a non-base character,
    /// swapping in their own idle/clap/bored/talking/disapproval clips in
    /// place of whatever clips the base controller's states currently use.
    /// </summary>
    public static void BuildOverrideController(
        string baseControllerPath, string savePath,
        string idleFbx, string clapFbx, string boredFbx, string talkingFbx, string disapprovalFbx)
    {
        AnimatorController baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(baseControllerPath);
        if (baseController == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: no base AnimatorController at {baseControllerPath}");
            return;
        }

        var newClipsByState = new Dictionary<string, AnimationClip>
        {
            ["Neutral"] = ExtractRealClip(idleFbx),
            ["Clapping"] = ExtractRealClip(clapFbx),
            ["Bored"] = ExtractRealClip(boredFbx),
            ["Engaged"] = ExtractRealClip(talkingFbx),
            ["Sympathetic"] = ExtractRealClip(talkingFbx),
            ["Distracted"] = ExtractRealClip(disapprovalFbx),
        };

        var stateNameByOriginalClip = new Dictionary<AnimationClip, string>();
        foreach (ChildAnimatorState child in baseController.layers[0].stateMachine.states)
        {
            if (child.state.motion is AnimationClip originalClip)
            {
                stateNameByOriginalClip[originalClip] = child.state.name;
            }
        }

        AnimatorOverrideController overrideController = new AnimatorOverrideController(baseController);

        var overridesList = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overridesList);
        for (int i = 0; i < overridesList.Count; i++)
        {
            AnimationClip original = overridesList[i].Key;
            if (original != null
                && stateNameByOriginalClip.TryGetValue(original, out string stateName)
                && newClipsByState.TryGetValue(stateName, out AnimationClip replacement)
                && replacement != null)
            {
                overridesList[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
            }
        }
        overrideController.ApplyOverrides(overridesList);

        AssetDatabase.CreateAsset(overrideController, savePath);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Instantiates masterFbx (its own Humanoid avatar already configured),
    /// scales it to targetHeight, attaches an Animator (with the given
    /// controller/override-controller) and AudienceMember, and saves the
    /// result as a prefab.
    /// </summary>
    public static void BuildCharacterPrefab(
        string masterFbx, string runtimeControllerPath,
        float targetHeight, string prefabSavePath)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(masterFbx);
        if (source == null)
        {
            Debug.LogError($"AudienceCharacterSetupUtility: no model at {masterFbx}");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float rawHeight = b.size.y;
            if (rawHeight > 0.0001f)
            {
                float scale = targetHeight / rawHeight;
                instance.transform.localScale = Vector3.one * scale;
            }
        }

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }
        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(masterFbx);
        animator.avatar = avatar;

        RuntimeAnimatorController runtimeController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(runtimeControllerPath);
        animator.runtimeAnimatorController = runtimeController;

        if (instance.GetComponent<AudienceMember>() == null)
        {
            instance.AddComponent<AudienceMember>();
        }

        instance.name = System.IO.Path.GetFileNameWithoutExtension(prefabSavePath);

        PrefabUtility.SaveAsPrefabAsset(instance, prefabSavePath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
    }
}
