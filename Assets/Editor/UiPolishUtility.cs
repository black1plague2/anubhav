using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual polish pass to bring the HUD and Coaching Report panel closer to
/// the reference mockups: rounded, glass-tinted panels instead of flat
/// rectangles, and one continuous top bar on the live HUD instead of
/// separate per-cluster backing boxes.
/// </summary>
public static class UiPolishUtility
{
    private const string RoundedSpritePath = "Assets/Textures/UI/RoundedPanel.png";
    private const int SpriteSize = 128;
    private const int CornerRadius = 34;

    /// <summary>Generates (once) a white, alpha-masked rounded-rect sprite with a 9-slice border, so any Image can be tinted per-use via Image.color while sharing one small texture.</summary>
    public static Sprite GetOrCreateRoundedSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
        if (existing != null)
        {
            return existing;
        }

        var tex = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
        float r = CornerRadius;
        float size = SpriteSize;

        for (int y = 0; y < SpriteSize; y++)
        {
            for (int x = 0; x < SpriteSize; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                // Distance outside the rounded rect (0 = on the edge, negative = inside away from any corner).
                float dx = Mathf.Max(0f, Mathf.Max(r - px, px - (size - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - py, py - (size - r)));
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - r;

                float alpha = Mathf.Clamp01(0.5f - dist); // ~1px antialiased edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(RoundedSpritePath));
        File.WriteAllBytes(RoundedSpritePath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(RoundedSpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(RoundedSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.spriteBorder = new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
    }

    /// <summary>Applies the rounded 9-sliced sprite (tinted to colorHex) to an Image. Pass an empty childName to round canvasName's own root Image instead of a child's.</summary>
    public static void RoundPanel(string canvasName, string childName, string colorHex)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"UiPolishUtility: canvas '{canvasName}' not found.");
            return;
        }

        GameObject targetGo;
        if (string.IsNullOrEmpty(childName))
        {
            targetGo = canvasGo;
        }
        else
        {
            Transform child = canvasGo.transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"UiPolishUtility: '{childName}' not found under '{canvasName}'.");
                return;
            }
            targetGo = child.gameObject;
        }

        Image image = targetGo.GetComponent<Image>();
        if (image == null)
        {
            Debug.LogError($"UiPolishUtility: '{targetGo.name}' has no Image component.");
            return;
        }

        image.sprite = GetOrCreateRoundedSprite();
        image.type = Image.Type.Sliced;
        if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
        {
            image.color = color;
        }

        EditorUtility.SetDirty(image);
    }

    /// <summary>
    /// Replaces the live HUD's separate per-cluster backing panels with one
    /// continuous rounded, glass-tinted top bar spanning Emotion/Score/Timer,
    /// matching the reference mockup's single curved strip instead of three
    /// disconnected boxes.
    /// </summary>
    public static void BuildUnifiedTopBar(string canvasName)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"UiPolishUtility: canvas '{canvasName}' not found.");
            return;
        }

        // Remove the old disconnected backing panels - superseded by TopBarPanel below.
        string[] oldBackings = { "ScoreText_Backing", "EmotionLabelText_Backing", "TimerText_Backing" };
        foreach (string name in oldBackings)
        {
            Transform t = canvasGo.transform.Find(name);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }

        if (canvasGo.transform.Find("TopBarPanel") == null)
        {
            GameObject barGo = new GameObject("TopBarPanel", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform, false);
            RectTransform barRect = barGo.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 1f);
            barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -10f);
            barRect.sizeDelta = new Vector2(760f, 150f);

            Image barImage = barGo.AddComponent<Image>();
            barImage.sprite = GetOrCreateRoundedSprite();
            barImage.type = Image.Type.Sliced;
            barImage.color = new Color(0.30f, 0.45f, 0.62f, 0.38f); // translucent glass-blue
            barImage.raycastTarget = false;

            // Render at the very back (below the old full-canvas AuraBackdrop, which this replaces as the emotion-color surface).
            barGo.transform.SetAsFirstSibling();

            // The reference HUD is one clean glass strip, not a full-screen
            // colour wash on top of it - retarget UIManager's aura tinting
            // at this bar directly instead of the old edge-to-edge backdrop,
            // and hide that backdrop so the two don't double-tint into mud.
            GameObject uiManagerGo = GameObject.Find("UIManager");
            if (uiManagerGo != null)
            {
                UIManager uiManager = uiManagerGo.GetComponent<UIManager>();
                if (uiManager != null)
                {
                    SerializedObject so = new SerializedObject(uiManager);
                    SerializedProperty prop = so.FindProperty("auraImage");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = barImage;
                        so.ApplyModifiedProperties();
                    }
                }
            }

            Transform oldAura = canvasGo.transform.Find("AuraBackdrop");
            if (oldAura != null)
            {
                oldAura.gameObject.SetActive(false);
            }
        }

        // Re-parent the existing text elements onto the new bar and re-lay them out
        // left/center/right within it, matching the mockup's single-strip layout.
        ReparentAndPosition(canvasGo.transform, "EmotionLabelText", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(220f, 40f), TextAlignmentOptions.Left);
        ReparentAndPosition(canvasGo.transform, "EmotionConfidenceText", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(220f, 30f), TextAlignmentOptions.Left);
        ReparentAndPosition(canvasGo.transform, "TimerText", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -35f), new Vector2(160f, 50f), TextAlignmentOptions.Right);

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    /// <summary>
    /// One-off fix for a HUD already built by BuildUnifiedTopBar: retargets
    /// UIManager.auraImage at the existing TopBarPanel and hides the old
    /// full-canvas AuraBackdrop, without touching anything already
    /// reparented onto the bar.
    /// </summary>
    public static void RetargetAuraToTopBar(string canvasName)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"UiPolishUtility: canvas '{canvasName}' not found.");
            return;
        }

        Transform bar = canvasGo.transform.Find("TopBarPanel");
        if (bar == null)
        {
            Debug.LogError("UiPolishUtility: 'TopBarPanel' not found - run BuildUnifiedTopBar first.");
            return;
        }
        Image barImage = bar.GetComponent<Image>();

        GameObject uiManagerGo = GameObject.Find("UIManager");
        if (uiManagerGo != null)
        {
            UIManager uiManager = uiManagerGo.GetComponent<UIManager>();
            if (uiManager != null)
            {
                SerializedObject so = new SerializedObject(uiManager);
                SerializedProperty prop = so.FindProperty("auraImage");
                if (prop != null)
                {
                    prop.objectReferenceValue = barImage;
                    so.ApplyModifiedProperties();
                }
            }
        }

        Transform oldAura = canvasGo.transform.Find("AuraBackdrop");
        if (oldAura != null)
        {
            oldAura.gameObject.SetActive(false);
        }

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    private static void ReparentAndPosition(Transform canvasTransform, string childName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, TextAlignmentOptions alignment)
    {
        Transform child = canvasTransform.Find(childName);
        if (child == null)
        {
            Debug.LogWarning($"UiPolishUtility: '{childName}' not found - skipping reposition.");
            return;
        }

        Transform topBar = canvasTransform.Find("TopBarPanel");
        child.SetParent(topBar != null ? topBar : canvasTransform, false);

        RectTransform rect = child.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.alignment = alignment;
        }
    }
}
