using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the real-time speaker feedback HUD: score, timer, emotion label +
/// confidence, an emotion-coded aura, a live transcript strip, and the
/// end-of-session Coaching Report panel. Other systems (HubClient, a session
/// controller, etc.) call the public Update*/Show* methods; this class owns
/// no networking or game logic of its own.
///
/// Emotion vocabulary is the real backend's fixed 6-label set (see
/// backend/app/services/emotion_service.py, UNIFIED_EMOTIONS): Confident,
/// Nervous, Bored, Excited, Monotone, Calm.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Score / Timer")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI timerText;
    [Tooltip("Optional radial-filled ring behind the score (Image Type = Filled, Fill Method = Radial 360).")]
    [SerializeField] private Image scoreRingImage;

    [Header("Emotion")]
    [SerializeField] private TextMeshProUGUI emotionLabelText;
    [SerializeField] private TextMeshProUGUI emotionConfidenceText;

    [Header("Aura (color-coded by emotion)")]
    [Tooltip("Optional UI backdrop/glow on the HUD canvas itself.")]
    [SerializeField] private Image auraImage;
    [Tooltip("Optional world-space glow around the player (e.g. a floor ring renderer).")]
    [SerializeField] private Renderer auraRenderer;
    [SerializeField] private float auraColorLerpSpeed = 4f;

    [Header("Pace / Filler (XAI) stats - live, session-scoped from HubClient if ever sent")]
    [SerializeField] private TextMeshProUGUI paceText;
    [SerializeField] private TextMeshProUGUI fillerText;

    [Header("Live Transcript Panel")]
    [SerializeField] private GameObject liveTranscriptPanelRoot;
    [SerializeField] private TextMeshProUGUI liveTranscriptText;
    [Tooltip("Longest transcript snippet kept on screen at once; older text is trimmed from the front.")]
    [SerializeField] private int liveTranscriptMaxChars = 160;

    [Header("Coaching Panel (in-session short tip)")]
    [SerializeField] private GameObject coachingPanelRoot;
    [SerializeField] private TextMeshProUGUI coachingText;
    [Tooltip("Seconds before the coaching panel auto-hides. 0 = stays until explicitly hidden.")]
    [SerializeField] private float coachingAutoHideSeconds = 6f;

    [Header("Session Report (end-of-session Coaching Report panel)")]
    [SerializeField] private GameObject sessionReportPanelRoot;
    [SerializeField] private TextMeshProUGUI reportFinalScoreText;
    [SerializeField] private Image reportInsightRingImage;
    [SerializeField] private TextMeshProUGUI reportInsightIndexText;
    [SerializeField] private TextMeshProUGUI reportPaceValueText;
    [SerializeField] private TextMeshProUGUI reportPaceTagText;
    [SerializeField] private Image reportPaceBarImage;
    [SerializeField] private TextMeshProUGUI reportFillerValueText;
    [SerializeField] private TextMeshProUGUI reportFillerTagText;
    [SerializeField] private Image reportFillerBarImage;
    [SerializeField] private TextMeshProUGUI reportStatBox1Label;
    [SerializeField] private TextMeshProUGUI reportStatBox1Value;
    [SerializeField] private TextMeshProUGUI reportStatBox2Label;
    [SerializeField] private TextMeshProUGUI reportStatBox2Value;
    [SerializeField] private TextMeshProUGUI reportStatBox3Label;
    [SerializeField] private TextMeshProUGUI reportStatBox3Value;
    [SerializeField] private TextMeshProUGUI reportCoachingSummaryText;
    [SerializeField] private TextMeshProUGUI reportNextStepsText;
    [SerializeField] private Button reportNextButton;
    [SerializeField] private Button reportCloseButton;

    [Header("Test (Inspector / ContextMenu)")]
    [SerializeField] private int testScore = 82;
    [SerializeField] private float testTimerSeconds = 65f;
    [SerializeField] private string testEmotion = "Confident";
    [SerializeField, Range(0f, 1f)] private float testConfidence = 0.87f;
    [SerializeField] private float testPaceWpm = 132f;
    [SerializeField] private int testFillerCount = 3;

    private Color _targetAuraColor = NeutralColor;
    private static readonly Color NeutralColor = new Color(0.55f, 0.70f, 0.95f);
    private const float AuraBackdropAlpha = 0.18f;
    private static readonly Regex ParenStatRegex = new Regex(@"\(([^)]*)\)", RegexOptions.Compiled);

    private Material _auraMaterialInstance;
    private SessionDetailData _currentReport;
    private int _currentInsightIndex;

    private void Awake()
    {
        if (auraRenderer != null)
        {
            // .material (not .sharedMaterial) instantiates a per-object copy
            // so lerping color here never touches the shared/prefab asset.
            _auraMaterialInstance = auraRenderer.material;
        }

        if (reportNextButton != null)
        {
            reportNextButton.onClick.AddListener(OnReportNextClicked);
        }
        if (reportCloseButton != null)
        {
            reportCloseButton.onClick.AddListener(HideSessionReport);
        }
        if (sessionReportPanelRoot != null)
        {
            sessionReportPanelRoot.SetActive(false);
        }
    }

    private void Update()
    {
        if (auraImage != null)
        {
            // GetAuraColor() returns fully opaque colors (they also drive the
            // opaque floor ring below) - force a low alpha here so the
            // full-canvas HUD backdrop stays a subtle tint instead of
            // eventually opaquing over the audience view after the first
            // emotion update.
            Color target = _targetAuraColor;
            target.a = AuraBackdropAlpha;
            auraImage.color = Color.Lerp(auraImage.color, target, Time.deltaTime * auraColorLerpSpeed);
        }

        if (_auraMaterialInstance != null)
        {
            _auraMaterialInstance.color = Color.Lerp(_auraMaterialInstance.color, _targetAuraColor, Time.deltaTime * auraColorLerpSpeed);
        }
    }

    /// <summary>Updates the top-center score readout and the radial score ring, if assigned.</summary>
    public void UpdateScore(int score)
    {
        if (scoreText != null)
        {
            scoreText.text = score.ToString();
        }

        if (scoreRingImage != null)
        {
            scoreRingImage.fillAmount = Mathf.Clamp01(score / 100f);
        }
    }

    /// <summary>Updates the top-right timer readout. Accepts elapsed/remaining seconds.</summary>
    public void UpdateTimer(float seconds)
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        timerText.text = $"{minutes:00}:{secs:00}";
    }

    /// <summary>
    /// Updates the top-left emotion label + confidence, and re-targets the
    /// aura color for the given emotion string (case-insensitive). Expects
    /// one of the backend's fixed 6 labels (Confident, Nervous, Bored,
    /// Excited, Monotone, Calm) but degrades gracefully for anything else.
    /// </summary>
    public void UpdateEmotion(string emotion, float confidence)
    {
        if (emotionLabelText != null)
        {
            emotionLabelText.text = emotion;
        }

        if (emotionConfidenceText != null)
        {
            emotionConfidenceText.text = $"{Mathf.RoundToInt(Mathf.Clamp01(confidence) * 100f)}%";
        }

        _targetAuraColor = GetAuraColor(emotion);
    }

    /// <summary>Updates the pace (words per minute) + filler-word count panel, when live values are available.</summary>
    public void UpdateXAI(float paceWpm, int fillerCount)
    {
        if (paceText != null)
        {
            paceText.text = $"{Mathf.RoundToInt(paceWpm)} WPM";
        }

        if (fillerText != null)
        {
            fillerText.text = $"Fillers: {fillerCount}";
        }
    }

    /// <summary>Updates the scrolling live-transcript strip. Trims to the most recent liveTranscriptMaxChars characters so the panel never overflows.</summary>
    public void UpdateLiveTranscript(string transcriptPartial)
    {
        if (liveTranscriptText == null || string.IsNullOrEmpty(transcriptPartial))
        {
            return;
        }

        string text = transcriptPartial;
        if (text.Length > liveTranscriptMaxChars)
        {
            text = "..." + text.Substring(text.Length - liveTranscriptMaxChars);
        }

        liveTranscriptText.text = text;

        if (liveTranscriptPanelRoot != null && !liveTranscriptPanelRoot.activeSelf)
        {
            liveTranscriptPanelRoot.SetActive(true);
        }
    }

    public void HideLiveTranscript()
    {
        if (liveTranscriptPanelRoot != null)
        {
            liveTranscriptPanelRoot.SetActive(false);
        }
    }

    /// <summary>Shows the short in-session coaching panel with the given tip and auto-hides it after coachingAutoHideSeconds (if > 0).</summary>
    public void ShowCoaching(string message)
    {
        if (coachingText != null)
        {
            coachingText.text = message;
        }

        if (coachingPanelRoot != null)
        {
            coachingPanelRoot.SetActive(true);
            CancelInvoke(nameof(HideCoaching));
            if (coachingAutoHideSeconds > 0f)
            {
                Invoke(nameof(HideCoaching), coachingAutoHideSeconds);
            }
        }
    }

    public void HideCoaching()
    {
        if (coachingPanelRoot != null)
        {
            coachingPanelRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Populates and shows the end-of-session "In-VR Coaching Panel" from a
    /// POST /session/complete (or GET /session/{id}) response. Every value
    /// shown here is either a direct field from SessionDetailData or a
    /// clearly-derived read of it (dominant emotion / average confidence
    /// from emotion_timeline, Good/Needs Work tags from SHAP contribution
    /// sign) - the backend does not stream a richer breakdown than this.
    /// </summary>
    public void ShowSessionReport(SessionDetailData report)
    {
        if (report == null || sessionReportPanelRoot == null)
        {
            return;
        }

        _currentReport = report;
        _currentInsightIndex = 0;

        if (reportFinalScoreText != null)
        {
            reportFinalScoreText.text = $"{report.overall_score}/100";
        }

        if (reportCoachingSummaryText != null)
        {
            reportCoachingSummaryText.text = report.coaching_text;
        }

        PopulateXaiRow(report, "pace", reportPaceValueText, reportPaceTagText, reportPaceBarImage);
        PopulateXaiRow(report, "filler", reportFillerValueText, reportFillerTagText, reportFillerBarImage);
        PopulateBottomStats(report);
        RenderCurrentInsight();


        sessionReportPanelRoot.SetActive(true);
    }

    public void HideSessionReport()
    {
        if (sessionReportPanelRoot != null)
        {
            sessionReportPanelRoot.SetActive(false);
        }
    }

    private void OnReportNextClicked()
    {
        if (_currentReport?.shap_breakdown == null || _currentReport.shap_breakdown.Count == 0)
        {
            HideSessionReport();
            return;
        }

        _currentInsightIndex++;
        if (_currentInsightIndex >= _currentReport.shap_breakdown.Count)
        {
            HideSessionReport();
            return;
        }

        RenderCurrentInsight();
    }

    private void RenderCurrentInsight()
    {
        List<ShapFeatureData> insights = _currentReport?.shap_breakdown;
        int count = insights?.Count ?? 0;

        if (reportInsightIndexText != null)
        {
            reportInsightIndexText.text = count > 0 ? $"{_currentInsightIndex + 1}/{count}" : "0/0";
        }

        if (reportInsightRingImage != null)
        {
            reportInsightRingImage.fillAmount = count > 0 ? (_currentInsightIndex + 1) / (float)count : 0f;
        }

        if (reportNextStepsText != null)
        {
            reportNextStepsText.text = count > 0 ? insights[_currentInsightIndex].explanation : "No further insights for this session.";
        }
    }

    private static void PopulateXaiRow(SessionDetailData report, string featureKeyword, TextMeshProUGUI valueText, TextMeshProUGUI tagText, Image barImage)
    {
        ShapFeatureData match = report.shap_breakdown?
            .FirstOrDefault(f => f.feature != null && f.feature.ToLowerInvariant().Contains(featureKeyword));

        if (match == null)
        {
            if (valueText != null) valueText.text = "--";
            if (tagText != null) tagText.text = "";
            if (barImage != null) barImage.fillAmount = 0.5f;
            return;
        }

        if (valueText != null)
        {
            valueText.text = ExtractParenthesizedStat(match.explanation) ?? $"{match.contribution:+0.0;-0.0} pts";
        }

        if (tagText != null)
        {
            tagText.text = match.contribution >= 2f ? "Good" : match.contribution <= -6f ? "Needs Work" : "Low";
        }

        if (barImage != null)
        {
            // Contribution points are centered on 0; map roughly [-10, +10] -> [0, 1] for a bar fill.
            barImage.fillAmount = Mathf.Clamp01(0.5f + match.contribution / 20f);
        }
    }

    /// <summary>Pulls the first "(...)" aside out of a SHAP explanation sentence (e.g. "(142 WPM)", "(4.2% fillers...)") as a compact display value.</summary>
    private static string ExtractParenthesizedStat(string explanation)
    {
        if (string.IsNullOrEmpty(explanation))
        {
            return null;
        }

        Match match = ParenStatRegex.Match(explanation);
        if (!match.Success)
        {
            return null;
        }

        string stat = match.Groups[1].Value.Trim();
        return stat.Length > 24 ? stat.Substring(0, 21) + "..." : stat;
    }

    /// <summary>
    /// Emotion -> aura color mapping using the backend's fixed 6-label
    /// vocabulary. Confident/Nervous are the two explicitly specified by the
    /// original spec; the rest follow the same "positive = cool/green,
    /// negative = warm/red" convention.
    /// </summary>
    private static Color GetAuraColor(string emotion)
    {
        switch (emotion?.Trim().ToLowerInvariant())
        {
            case "confident":
                return new Color(0.20f, 0.85f, 0.35f); // green
            case "nervous":
                return new Color(0.95f, 0.85f, 0.15f); // yellow
            case "excited":
                return new Color(1.00f, 0.55f, 0.10f); // orange
            case "bored":
                return new Color(0.50f, 0.50f, 0.55f); // neutral gray
            case "monotone":
                return new Color(0.45f, 0.55f, 0.70f); // muted slate blue
            case "calm":
            default:
                return NeutralColor; // soft blue
        }
    }

    [ContextMenu("Test/Run Sample Update")]
    private void TestRunSampleUpdate()
    {
        UpdateScore(testScore);
        UpdateTimer(testTimerSeconds);
        UpdateEmotion(testEmotion, testConfidence);
        UpdateXAI(testPaceWpm, testFillerCount);
    }

    [ContextMenu("Test/Show Sample Coaching Tip")]
    private void TestShowSampleCoaching()
    {
        ShowCoaching("Try slowing down slightly - you're averaging " + Mathf.RoundToInt(testPaceWpm) + " WPM.");
    }

    [ContextMenu("Test/Show Sample Live Transcript")]
    private void TestShowSampleLiveTranscript()
    {
        UpdateLiveTranscript("...and that's how we solved the latency issue directly with streaming WebSockets.");
    }

    [ContextMenu("Test/Show Sample Session Report")]
    private void TestShowSampleSessionReport()
    {
        var sample = new SessionDetailData
        {
            session_id = "test",
            overall_score = testScore,
            coaching_text = "Solid pacing throughout the opening. Keep reducing filler words during transitions.",
            emotion_timeline = new List<EmotionPointData>
            {
                new EmotionPointData { time = 5f, emotion = "confident", intensity = 0.82f },
                new EmotionPointData { time = 15f, emotion = "calm", intensity = 0.75f },
                new EmotionPointData { time = 28f, emotion = "confident", intensity = 0.85f },
            },
            shap_breakdown = new List<ShapFeatureData>
            {
                new ShapFeatureData { feature = "Filler Words", contribution = -8.3f, explanation = "High filler density (4.2% fillers) lowered your score by 8.3 points." },
                new ShapFeatureData { feature = "Speaking Pace (WPM)", contribution = 6.1f, explanation = "Ideal cadence (142 WPM) maintained audience engagement." },
                new ShapFeatureData { feature = "Pause Duration", contribution = 4.5f, explanation = "Deliberate pauses (4.8s total) gave key arguments time to land." },
            },
        };
        ShowSessionReport(sample);
    }

    private void PopulateBottomStats(SessionDetailData report)
    {
        List<EmotionPointData> timeline = report.emotion_timeline;
        string dominantEmotion = "--";
        float avgConfidence = 0f;

        if (timeline != null && timeline.Count > 0)
        {
            dominantEmotion = timeline
                .GroupBy(p => p.emotion)
                .OrderByDescending(g => g.Count())
                .First().Key;
            avgConfidence = timeline.Average(p => p.intensity);
        }

        if (reportStatBox1Label != null) reportStatBox1Label.text = "Dominant Emotion";
        if (reportStatBox1Value != null) reportStatBox1Value.text = dominantEmotion;

        if (reportStatBox2Label != null) reportStatBox2Label.text = "Avg Confidence";
        if (reportStatBox2Value != null) reportStatBox2Value.text = $"{Mathf.RoundToInt(avgConfidence * 100f)}%";

        if (reportStatBox3Label != null) reportStatBox3Label.text = "Insights";
        if (reportStatBox3Value != null) reportStatBox3Value.text = (report.shap_breakdown?.Count ?? 0).ToString();
    }
}
