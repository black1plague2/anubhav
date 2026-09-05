using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-off editor builder for the real-time speaker feedback HUD. Everything
/// is constructed with real C# API calls (no cross-boundary object
/// references needed), then invoked as a single static method from tooling.
/// </summary>
public static class FeedbackHudSetupUtility
{
    private const float CanvasWidthUnits = 800f;
    private const float CanvasHeightUnits = 500f;
    private const float CanvasWorldScale = 0.00125f; // -> 1.0m x 0.625m physical panel

    public static void BuildFeedbackHud(string cameraAnchorName, string oldTimerCanvasNameToRemove)
    {
        if (!string.IsNullOrEmpty(oldTimerCanvasNameToRemove))
        {
            GameObject oldCanvas = GameObject.Find(oldTimerCanvasNameToRemove);
            if (oldCanvas != null)
            {
                Object.DestroyImmediate(oldCanvas);
            }
        }

        GameObject canvasGo = new GameObject("FeedbackHUD_Canvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(CanvasWidthUnits, CanvasHeightUnits);
        canvasGo.transform.localScale = Vector3.one * CanvasWorldScale;

        GameObject anchor = string.IsNullOrEmpty(cameraAnchorName) ? null : GameObject.Find(cameraAnchorName);
        if (anchor != null)
        {
            canvasGo.transform.SetParent(anchor.transform, false);
            // Arm's-length, slightly below eye line so it doesn't block the audience view.
            canvasGo.transform.localPosition = new Vector3(0f, -0.12f, 0.55f);
            canvasGo.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogWarning($"FeedbackHudSetupUtility: camera anchor '{cameraAnchorName}' not found - canvas left at world origin.");
        }

        // Backdrop / aura glow behind all HUD text, tinted per-emotion at runtime.
        GameObject auraGo = new GameObject("AuraBackdrop", typeof(RectTransform));
        auraGo.transform.SetParent(canvasGo.transform, false);
        RectTransform auraRect = auraGo.GetComponent<RectTransform>();
        auraRect.anchorMin = Vector2.zero;
        auraRect.anchorMax = Vector2.one;
        auraRect.offsetMin = Vector2.zero;
        auraRect.offsetMax = Vector2.zero;
        Image auraImage = auraGo.AddComponent<Image>();
        auraImage.color = new Color(0.55f, 0.70f, 0.95f, 0.15f);
        auraImage.raycastTarget = false;

        TextMeshProUGUI scoreText = MakeText(
            "ScoreText", canvasGo.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(320f, 90f),
            48f, TextAlignmentOptions.Center, Color.white, "Score: 0");

        TextMeshProUGUI timerText = MakeText(
            "TimerText", canvasGo.transform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-130f, -45f), new Vector2(220f, 80f),
            42f, TextAlignmentOptions.Center, Color.white, "00:00");

        TextMeshProUGUI emotionLabelText = MakeText(
            "EmotionLabelText", canvasGo.transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(130f, -35f), new Vector2(240f, 55f),
            34f, TextAlignmentOptions.Center, Color.white, "Neutral");

        TextMeshProUGUI emotionConfidenceText = MakeText(
            "EmotionConfidenceText", canvasGo.transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(130f, -85f), new Vector2(240f, 40f),
            26f, TextAlignmentOptions.Center, new Color(0.85f, 0.85f, 0.85f), "0%");

        TextMeshProUGUI paceText = MakeText(
            "PaceText", canvasGo.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(130f, 55f), new Vector2(240f, 44f),
            28f, TextAlignmentOptions.Center, Color.white, "0 WPM");

        TextMeshProUGUI fillerText = MakeText(
            "FillerText", canvasGo.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(130f, 15f), new Vector2(240f, 34f),
            24f, TextAlignmentOptions.Center, new Color(0.90f, 0.60f, 0.60f), "Fillers: 0");

        GameObject coachingPanelGo = new GameObject("CoachingPanel", typeof(RectTransform));
        coachingPanelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform coachingRect = coachingPanelGo.GetComponent<RectTransform>();
        coachingRect.anchorMin = new Vector2(0.5f, 0f);
        coachingRect.anchorMax = new Vector2(0.5f, 0f);
        coachingRect.pivot = new Vector2(0.5f, 0f);
        coachingRect.anchoredPosition = new Vector2(0f, -60f);
        coachingRect.sizeDelta = new Vector2(600f, 90f);
        Image coachingBackdrop = coachingPanelGo.AddComponent<Image>();
        coachingBackdrop.color = new Color(0.05f, 0.05f, 0.08f, 0.75f);
        coachingBackdrop.raycastTarget = false;
        coachingPanelGo.SetActive(false);

        TextMeshProUGUI coachingText = MakeText(
            "CoachingText", coachingPanelGo.transform,
            Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero,
            26f, TextAlignmentOptions.Center, new Color(1f, 0.92f, 0.70f), "Coaching tip");
        RectTransform coachingTextRect = coachingText.GetComponent<RectTransform>();
        coachingTextRect.offsetMin = new Vector2(20f, 10f);
        coachingTextRect.offsetMax = new Vector2(-20f, -10f);

        // Flat world-space glow ring on the floor around the player, tinted per-emotion at runtime.
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "PlayerAuraRing";
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.position = new Vector3(0f, 0.02f, 0f);
        ring.transform.localScale = new Vector3(3.0f, 0.01f, 3.0f);
        Renderer ringRenderer = ring.GetComponent<Renderer>();
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Material ringMaterial = new Material(unlitShader) { color = new Color(0.55f, 0.70f, 0.95f, 0.35f) };
        ringRenderer.sharedMaterial = ringMaterial;

        GameObject uiManagerGo = new GameObject("UIManager");
        UIManager uiManager = uiManagerGo.AddComponent<UIManager>();

        SerializedObject so = new SerializedObject(uiManager);
        SetRef(so, "scoreText", scoreText);
        SetRef(so, "timerText", timerText);
        SetRef(so, "emotionLabelText", emotionLabelText);
        SetRef(so, "emotionConfidenceText", emotionConfidenceText);
        SetRef(so, "auraImage", auraImage);
        SetRef(so, "auraRenderer", ringRenderer);
        SetRef(so, "paceText", paceText);
        SetRef(so, "fillerText", fillerText);
        SetRef(so, "coachingPanelRoot", coachingPanelGo);
        SetRef(so, "coachingText", coachingText);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(uiManagerGo);
        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(uiManagerGo.scene);
    }

    private static void SetRef(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"FeedbackHudSetupUtility: serialized property '{fieldName}' not found on UIManager.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    /// <summary>
    /// Visual polish pass on an already-built HUD: adds a rounded-feel dark
    /// backing panel behind each text cluster (Score/Timer/Emotion/Pace) so
    /// text stays legible over any background, and bumps font weight for
    /// arm's-length VR readability.
    /// </summary>
    public static void PolishHud(string canvasName)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"FeedbackHudSetupUtility: canvas '{canvasName}' not found.");
            return;
        }

        AddBackingPanel(canvasGo.transform, "ScoreText_Backing", "ScoreText", 40f, 20f);
        AddBackingPanel(canvasGo.transform, "TimerText_Backing", "TimerText", 30f, 18f);
        AddBackingPanel(canvasGo.transform, "EmotionLabelText_Backing", "EmotionLabelText", 30f, 55f, "EmotionConfidenceText");
        AddBackingPanel(canvasGo.transform, "PaceText_Backing", "PaceText", 30f, 45f, "FillerText");

        BoldenText(canvasGo.transform, "ScoreText");
        BoldenText(canvasGo.transform, "TimerText");
        BoldenText(canvasGo.transform, "EmotionLabelText");
    }

    private static void BoldenText(Transform canvasTransform, string childName)
    {
        Transform t = canvasTransform.Find(childName);
        if (t == null) return;
        TextMeshProUGUI tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.fontStyle = FontStyles.Bold;
        }
    }

    /// <summary>
    /// Creates a dark rounded-feel panel sized to cover primaryChild's
    /// RectTransform (padded), optionally extended downward to also cover
    /// secondaryChild, and inserts it as the first sibling under
    /// primaryChild's parent so it renders behind the text.
    /// </summary>
    private static void AddBackingPanel(
        Transform canvasTransform, string panelName,
        string primaryChild, float paddingX, float extraHeight,
        string secondaryChild = null)
    {
        if (canvasTransform.Find(panelName) != null)
        {
            return; // already polished, don't duplicate
        }

        Transform primary = canvasTransform.Find(primaryChild);
        if (primary == null) return;
        RectTransform primaryRect = primary.GetComponent<RectTransform>();

        GameObject panelGo = new GameObject(panelName, typeof(RectTransform));
        panelGo.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = primaryRect.anchorMin;
        panelRect.anchorMax = primaryRect.anchorMax;
        panelRect.pivot = primaryRect.pivot;
        panelRect.anchoredPosition = primaryRect.anchoredPosition + new Vector2(0f, secondaryChild != null ? -extraHeight * 0.5f : 0f);
        panelRect.sizeDelta = new Vector2(
            primaryRect.sizeDelta.x + paddingX,
            primaryRect.sizeDelta.y + extraHeight);

        Image panelImage = panelGo.AddComponent<Image>();
        panelImage.color = new Color(0.03f, 0.03f, 0.05f, 0.55f);
        panelImage.raycastTarget = false;

        panelGo.transform.SetAsFirstSibling();
        // Keep it above the full-canvas AuraBackdrop (which is even further back).
        Transform aura = canvasTransform.Find("AuraBackdrop");
        if (aura != null)
        {
            panelGo.transform.SetSiblingIndex(aura.GetSiblingIndex() + 1);
        }
    }

    /// <summary>Finds the scene's existing UIManager GameObject/component, used by all the incremental HUD builders below (score ring, live transcript, session report) that extend an already-built HUD rather than constructing it from scratch.</summary>
    private static UIManager FindUiManager()
    {
        GameObject go = GameObject.Find("UIManager");
        if (go == null)
        {
            Debug.LogError("FeedbackHudSetupUtility: 'UIManager' GameObject not found - build the base HUD first.");
            return null;
        }

        UIManager uiManager = go.GetComponent<UIManager>();
        if (uiManager == null)
        {
            Debug.LogError("FeedbackHudSetupUtility: 'UIManager' GameObject has no UIManager component.");
        }
        return uiManager;
    }

    /// <summary>Adds a radial-fill ring behind ScoreText (Image Type=Filled, Radial360) and wires it to UIManager.scoreRingImage, so the score readout matches the "circular score meter" reference design.</summary>
    public static void AddScoreRing(string canvasName)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"FeedbackHudSetupUtility: canvas '{canvasName}' not found.");
            return;
        }

        Transform scoreText = canvasGo.transform.Find("ScoreText");
        if (scoreText == null)
        {
            Debug.LogError("FeedbackHudSetupUtility: 'ScoreText' not found under canvas.");
            return;
        }

        if (canvasGo.transform.Find("ScoreRing") != null)
        {
            return; // already added
        }

        RectTransform scoreRect = scoreText.GetComponent<RectTransform>();

        GameObject ringGo = new GameObject("ScoreRing", typeof(RectTransform));
        ringGo.transform.SetParent(canvasGo.transform, false);
        RectTransform ringRect = ringGo.GetComponent<RectTransform>();
        ringRect.anchorMin = scoreRect.anchorMin;
        ringRect.anchorMax = scoreRect.anchorMax;
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.anchoredPosition = scoreRect.anchoredPosition;
        ringRect.sizeDelta = new Vector2(140f, 140f);

        Image ringImage = ringGo.AddComponent<Image>();
        ringImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        ringImage.type = Image.Type.Filled;
        ringImage.fillMethod = Image.FillMethod.Radial360;
        ringImage.fillOrigin = (int)Image.Origin360.Top;
        ringImage.fillClockwise = true;
        ringImage.fillAmount = 0f;
        ringImage.color = new Color(0.20f, 0.85f, 0.35f); // matches the "confident/green" default aura tone
        ringImage.raycastTarget = false;

        // Render behind the score number, in front of its backing panel.
        ringGo.transform.SetSiblingIndex(scoreText.GetSiblingIndex());

        UIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            SerializedObject so = new SerializedObject(uiManager);
            SetRef(so, "scoreRingImage", ringImage);
            so.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    /// <summary>Adds a bottom strip "Live transcript" panel to the HUD canvas, matching the reference design's scrolling caption bar under the main curved panel.</summary>
    public static void BuildLiveTranscriptPanel(string canvasName)
    {
        GameObject canvasGo = GameObject.Find(canvasName);
        if (canvasGo == null)
        {
            Debug.LogError($"FeedbackHudSetupUtility: canvas '{canvasName}' not found.");
            return;
        }

        if (canvasGo.transform.Find("LiveTranscriptPanel") != null)
        {
            return; // already added
        }

        GameObject panelGo = new GameObject("LiveTranscriptPanel", typeof(RectTransform));
        panelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, -170f);
        panelRect.sizeDelta = new Vector2(760f, 90f);
        Image panelBackdrop = panelGo.AddComponent<Image>();
        panelBackdrop.color = new Color(0.03f, 0.05f, 0.09f, 0.72f);
        panelBackdrop.raycastTarget = false;
        panelGo.SetActive(false);

        TextMeshProUGUI headerText = MakeText(
            "LiveTranscriptHeader", panelGo.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -14f), new Vector2(-20f, 24f),
            18f, TextAlignmentOptions.TopLeft, new Color(0.6f, 0.75f, 0.9f), "Live transcript");
        RectTransform headerRect = headerText.GetComponent<RectTransform>();
        headerRect.offsetMin = new Vector2(16f, headerRect.offsetMin.y);

        TextMeshProUGUI transcriptText = MakeText(
            "LiveTranscriptText", panelGo.transform,
            Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero,
            22f, TextAlignmentOptions.TopLeft, Color.white, "");
        RectTransform transcriptRect = transcriptText.GetComponent<RectTransform>();
        transcriptRect.offsetMin = new Vector2(16f, 8f);
        transcriptRect.offsetMax = new Vector2(-16f, -32f);
        transcriptText.textWrappingMode = TextWrappingModes.Normal;
        transcriptText.overflowMode = TextOverflowModes.Truncate;

        UIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            SerializedObject so = new SerializedObject(uiManager);
            SetRef(so, "liveTranscriptPanelRoot", panelGo);
            SetRef(so, "liveTranscriptText", transcriptText);
            so.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    /// <summary>
    /// Builds the end-of-session "In-VR Coaching Panel" as its own world-space
    /// canvas (a modal report, distinct from the slim always-on HUD), parented
    /// to the same camera anchor as FeedbackHUD_Canvas. Populated at runtime
    /// by UIManager.ShowSessionReport() from a POST /session/complete response.
    /// </summary>
    public static void BuildSessionReportPanel(string cameraAnchorName)
    {
        if (GameObject.Find("SessionReportCanvas") != null)
        {
            Debug.LogWarning("FeedbackHudSetupUtility: 'SessionReportCanvas' already exists - skipping.");
            return;
        }

        GameObject canvasGo = new GameObject("SessionReportCanvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(900f, 620f);
        canvasGo.transform.localScale = Vector3.one * CanvasWorldScale;

        GameObject anchor = string.IsNullOrEmpty(cameraAnchorName) ? null : GameObject.Find(cameraAnchorName);
        if (anchor != null)
        {
            canvasGo.transform.SetParent(anchor.transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.9f); // comfortable reading distance, dead-ahead
            canvasGo.transform.localRotation = Quaternion.identity;
        }

        Image backdrop = canvasGo.AddComponent<Image>();
        backdrop.color = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        // --- Title bar ---------------------------------------------------
        MakeText("ReportTitleText", canvasGo.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -30f), new Vector2(-90f, 50f),
            32f, TextAlignmentOptions.Left, Color.white, "In-VR Coaching Panel");

        Button closeButton = MakeButton("ReportCloseButton", canvasGo.transform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-40f, -30f), new Vector2(56f, 56f),
            new Color(0.15f, 0.16f, 0.20f), "X", 28f);

        MakeText("ReportCoachingSummaryText", canvasGo.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -75f), new Vector2(-40f, 34f),
            18f, TextAlignmentOptions.Left, new Color(0.85f, 0.85f, 0.9f), "");

        // --- Final Score box ----------------------------------------------
        GameObject scoreBox = MakeBox("ReportFinalScoreBox", canvasGo.transform,
            new Vector2(-300f, 90f), new Vector2(240f, 180f));
        MakeText("ReportFinalScoreLabel", scoreBox.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -24f), new Vector2(0f, 30f),
            20f, TextAlignmentOptions.Center, new Color(0.75f, 0.78f, 0.85f), "Final Score");
        TextMeshProUGUI finalScoreValue = MakeText("ReportFinalScoreValue", scoreBox.transform,
            Vector2.zero, Vector2.one,
            new Vector2(0f, -10f), Vector2.zero,
            46f, TextAlignmentOptions.Center, Color.white, "0/100");

        // --- Insight progress ring ------------------------------------------
        GameObject ringGo = new GameObject("ReportInsightRing", typeof(RectTransform));
        ringGo.transform.SetParent(canvasGo.transform, false);
        RectTransform ringRect = ringGo.GetComponent<RectTransform>();
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.anchoredPosition = new Vector2(0f, 90f);
        ringRect.sizeDelta = new Vector2(150f, 150f);
        Image ringImage = ringGo.AddComponent<Image>();
        ringImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        ringImage.type = Image.Type.Filled;
        ringImage.fillMethod = Image.FillMethod.Radial360;
        ringImage.fillOrigin = (int)Image.Origin360.Top;
        ringImage.color = new Color(0.20f, 0.85f, 0.35f);
        ringImage.raycastTarget = false;
        TextMeshProUGUI insightIndexText = MakeText("ReportInsightIndexText", ringGo.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            26f, TextAlignmentOptions.Center, Color.white, "0/0");

        // --- XAI box (Pace / Filler) -----------------------------------
        GameObject xaiBox = MakeBox("ReportXaiBox", canvasGo.transform,
            new Vector2(300f, 90f), new Vector2(280f, 180f));
        MakeText("ReportXaiHeader", xaiBox.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -22f), new Vector2(-16f, 28f),
            20f, TextAlignmentOptions.Left, new Color(0.75f, 0.78f, 0.85f), "XAI");

        (TextMeshProUGUI paceValue, TextMeshProUGUI paceTag, Image paceBar) =
            MakeXaiRow(xaiBox.transform, "ReportPace", "Pace", 20f);
        (TextMeshProUGUI fillerValue, TextMeshProUGUI fillerTag, Image fillerBar) =
            MakeXaiRow(xaiBox.transform, "ReportFiller", "Filler Rate", -50f);

        // --- Bottom stat row --------------------------------------------
        (TextMeshProUGUI stat1Label, TextMeshProUGUI stat1Value) = MakeStatBox(canvasGo.transform, "ReportStatBox1", -280f);
        (TextMeshProUGUI stat2Label, TextMeshProUGUI stat2Value) = MakeStatBox(canvasGo.transform, "ReportStatBox2", 0f);
        (TextMeshProUGUI stat3Label, TextMeshProUGUI stat3Value) = MakeStatBox(canvasGo.transform, "ReportStatBox3", 280f);

        // --- Next Steps card ----------------------------------------------
        GameObject nextStepsBox = MakeBox("ReportNextStepsBox", canvasGo.transform,
            new Vector2(-70f, -230f), new Vector2(680f, 150f));
        MakeText("ReportNextStepsHeader", nextStepsBox.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -22f), new Vector2(-20f, 28f),
            20f, TextAlignmentOptions.Left, new Color(0.75f, 0.78f, 0.85f), "Next Steps");
        TextMeshProUGUI nextStepsText = MakeText("ReportNextStepsText", nextStepsBox.transform,
            Vector2.zero, Vector2.one,
            new Vector2(0f, -14f), Vector2.zero,
            19f, TextAlignmentOptions.TopLeft, Color.white, "");
        RectTransform nextStepsTextRect = nextStepsText.GetComponent<RectTransform>();
        nextStepsTextRect.offsetMin = new Vector2(20f, 16f);
        nextStepsTextRect.offsetMax = new Vector2(-20f, -46f);
        nextStepsText.textWrappingMode = TextWrappingModes.Normal;

        Button nextButton = MakeButton("ReportNextButton", canvasGo.transform,
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-100f, -230f), new Vector2(160f, 60f),
            new Color(0.20f, 0.75f, 0.45f), "Next", 24f);

        canvasGo.SetActive(false);

        UIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            SerializedObject so = new SerializedObject(uiManager);
            SetRef(so, "sessionReportPanelRoot", canvasGo);
            SetRef(so, "reportFinalScoreText", finalScoreValue);
            SetRef(so, "reportInsightRingImage", ringImage);
            SetRef(so, "reportInsightIndexText", insightIndexText);
            SetRef(so, "reportPaceValueText", paceValue);
            SetRef(so, "reportPaceTagText", paceTag);
            SetRef(so, "reportPaceBarImage", paceBar);
            SetRef(so, "reportFillerValueText", fillerValue);
            SetRef(so, "reportFillerTagText", fillerTag);
            SetRef(so, "reportFillerBarImage", fillerBar);
            SetRef(so, "reportStatBox1Label", stat1Label);
            SetRef(so, "reportStatBox1Value", stat1Value);
            SetRef(so, "reportStatBox2Label", stat2Label);
            SetRef(so, "reportStatBox2Value", stat2Value);
            SetRef(so, "reportStatBox3Label", stat3Label);
            SetRef(so, "reportStatBox3Value", stat3Value);
            SetRef(so, "reportCoachingSummaryText", canvasGo.transform.Find("ReportCoachingSummaryText").GetComponent<TextMeshProUGUI>());
            SetRef(so, "reportNextStepsText", nextStepsText);
            SetRef(so, "reportNextButton", nextButton);
            SetRef(so, "reportCloseButton", closeButton);
            so.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    private static GameObject MakeBox(string name, Transform parent, Vector2 centeredPosition, Vector2 size)
    {
        GameObject boxGo = new GameObject(name, typeof(RectTransform));
        boxGo.transform.SetParent(parent, false);
        RectTransform rect = boxGo.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = centeredPosition;
        rect.sizeDelta = size;
        Image image = boxGo.AddComponent<Image>();
        image.color = new Color(0.10f, 0.11f, 0.15f, 0.85f);
        image.raycastTarget = false;
        return boxGo;
    }

    private static (TextMeshProUGUI label, TextMeshProUGUI value) MakeStatBox(Transform canvasTransform, string name, float x)
    {
        GameObject box = MakeBox(name, canvasTransform, new Vector2(x, -110f), new Vector2(240f, 110f));
        TextMeshProUGUI value = MakeText($"{name}Value", box.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -18f), new Vector2(0f, 40f),
            26f, TextAlignmentOptions.Center, Color.white, "--");
        TextMeshProUGUI label = MakeText($"{name}Label", box.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 16f), new Vector2(0f, 26f),
            15f, TextAlignmentOptions.Center, new Color(0.7f, 0.73f, 0.8f), "");
        return (label, value);
    }

    private static (TextMeshProUGUI value, TextMeshProUGUI tag, Image bar) MakeXaiRow(Transform xaiBoxTransform, string name, string rowLabel, float y)
    {
        MakeText($"{name}Label", xaiBoxTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, y - 4f), new Vector2(-16f, 22f),
            16f, TextAlignmentOptions.Left, new Color(0.7f, 0.73f, 0.8f), rowLabel);

        TextMeshProUGUI valueText = MakeText($"{name}ValueText", xaiBoxTransform,
            new Vector2(0f, 1f), new Vector2(0.6f, 1f),
            new Vector2(16f, y - 26f), new Vector2(-8f, 26f),
            20f, TextAlignmentOptions.Left, Color.white, "--");

        TextMeshProUGUI tagText = MakeText($"{name}TagText", xaiBoxTransform,
            new Vector2(0.6f, 1f), new Vector2(1f, 1f),
            new Vector2(8f, y - 26f), new Vector2(-16f, 26f),
            16f, TextAlignmentOptions.Right, new Color(0.4f, 0.9f, 0.5f), "");

        GameObject barBg = new GameObject($"{name}BarBg", typeof(RectTransform));
        barBg.transform.SetParent(xaiBoxTransform, false);
        RectTransform barBgRect = barBg.GetComponent<RectTransform>();
        barBgRect.anchorMin = new Vector2(0f, 1f);
        barBgRect.anchorMax = new Vector2(1f, 1f);
        barBgRect.pivot = new Vector2(0f, 1f);
        barBgRect.anchoredPosition = new Vector2(16f, y - 50f);
        barBgRect.sizeDelta = new Vector2(-32f, 10f);
        Image barBgImage = barBg.AddComponent<Image>();
        barBgImage.color = new Color(1f, 1f, 1f, 0.12f);
        barBgImage.raycastTarget = false;

        GameObject barFill = new GameObject($"{name}BarFill", typeof(RectTransform));
        barFill.transform.SetParent(barBg.transform, false);
        RectTransform barFillRect = barFill.GetComponent<RectTransform>();
        barFillRect.anchorMin = Vector2.zero;
        barFillRect.anchorMax = Vector2.one;
        barFillRect.offsetMin = Vector2.zero;
        barFillRect.offsetMax = Vector2.zero;
        Image barFillImage = barFill.AddComponent<Image>();
        barFillImage.color = new Color(0.20f, 0.85f, 0.35f);
        barFillImage.type = Image.Type.Filled;
        barFillImage.fillMethod = Image.FillMethod.Horizontal;
        barFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        barFillImage.fillAmount = 0.5f;
        barFillImage.raycastTarget = false;

        return (valueText, tagText, barFillImage);
    }

    private static Button MakeButton(
        string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta,
        Color backgroundColor, string label, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        Image image = go.AddComponent<Image>();
        image.color = backgroundColor;

        Button button = go.AddComponent<Button>();

        MakeText($"{name}_Label", go.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            fontSize, TextAlignmentOptions.Center, Color.white, label);

        return button;
    }

    private static TextMeshProUGUI MakeText(
        string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta,
        float fontSize, TextAlignmentOptions alignment, Color color, string initialText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.text = initialText;
        tmp.raycastTarget = false;

        return tmp;
    }
}
