using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Wire schemas for the real Anubhav FastAPI hub (github.com/vaibhav700c/Anubhav,
/// backend/app/schemas + backend/app/hub.py + backend/app/routes/session.py).
/// These match the actual backend code, not the aspirational build-spec doc -
/// verified directly against hub.py's coach_feedback/live_frame payloads and
/// session_schemas.py's Pydantic models. Field names are snake_case to match
/// the JSON on the wire exactly; Newtonsoft.Json (already resolved into this
/// project via com.unity.nuget.newtonsoft-json) is used instead of JsonUtility
/// because several fields here are optional/nullable, which JsonUtility
/// handles poorly.
/// </summary>
[Serializable]
public class TypeOnlyMessage
{
    /// <summary>"coach_feedback" | "pong" | anything else the hub might add later.</summary>
    public string type;
}

/// <summary>
/// Outbound -> VR message (hub.py send_to_vr): one JSON frame per processed
/// chunk, immediately followed (same call) by raw Bulbul TTS audio bytes as a
/// separate binary WebSocket frame. There is no separate "emotion"/"score"
/// message type on this channel - score, emotion and the coaching line always
/// arrive together.
/// </summary>
[Serializable]
public class CoachFeedbackMessage
{
    public string type; // "coach_feedback"
    public int score;
    /// <summary>One of the fixed 6 labels: Confident, Nervous, Bored, Excited, Monotone, Calm.</summary>
    public string emotion;
    public string coaching_text;
}

/// <summary>
/// Outbound -> Flutter app telemetry frame (LiveUpdateSchema / hub.py live_frame).
/// Unity does not normally receive this on its client_type=vr socket - HubClient
/// opens a second, receive-only client_type=app socket purely to pick up
/// transcript_partial for the in-VR live transcript panel, since the vr channel
/// never carries a transcript.
/// </summary>
[Serializable]
public class LiveUpdateMessage
{
    public int score;
    public string emotion_label;
    public string transcript_partial;
    public string coaching_tip;
    public bool is_final;
    public string disclaimer;
}

[Serializable]
public class ShapFeatureData
{
    public string feature;
    public float contribution;
    public string explanation;
}

[Serializable]
public class EmotionPointData
{
    public float time;
    public string emotion;
    public float intensity = 1f;
}

/// <summary>Matches SessionDetailSchema - the shape returned by both POST /session/complete (nested under "session") and GET /session/{id}.</summary>
[Serializable]
public class SessionDetailData
{
    public string session_id;
    public string user_id;
    public string date;
    public int overall_score;
    public List<EmotionPointData> emotion_timeline = new List<EmotionPointData>();
    public List<ShapFeatureData> shap_breakdown = new List<ShapFeatureData>();
    public string transcript;
    public string coaching_text;
    public string disclaimer;
    public string topic;
    public string language;
}

[Serializable]
public class SessionCompleteResponseData
{
    public string status;
    public SessionDetailData session;
    public string disclaimer;
}

/// <summary>Matches SessionCompleteRequest - the body HubClient POSTs to /session/complete.</summary>
[Serializable]
public class SessionCompleteRequestData
{
    public string session_id;
    public string user_id;
    public string final_transcript;
    public string topic;
    public string language;
    public string audience_size;
}
