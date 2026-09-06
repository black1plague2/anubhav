using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Owns the connection to the real Anubhav FastAPI hub
/// (github.com/vaibhav700c/Anubhav, backend/app/hub.py + routes/session.py).
///
/// Contract, verified directly against that repo's source (not the aspirational
/// build-spec doc, which describes a richer message set the current backend
/// does not actually send):
///   - WS /session/{session_id}?client_type=vr : Unity's primary channel.
///     Send: raw binary PCM16 audio frames. Receive: one {"type":"coach_feedback",
///     "score","emotion","coaching_text"} JSON frame per processed chunk,
///     immediately followed by raw Bulbul TTS audio bytes (a WAV file, not
///     headerless PCM) as a separate binary frame.
///   - WS /session/{session_id}?client_type=app : the Flutter telemetry
///     channel. It carries transcript_partial, score and emotion_label on
///     every processed audio window (~3.5s, hub.py's PIPELINE_WINDOW_SEC) -
///     far more often than the vr channel's coach_feedback, which only
///     arrives once the full LLM+TTS coaching round-trip completes
///     (commonly 20-30s, since sarvam-105b reasons internally before
///     answering). This client opens a second, receive-only connection on
///     that channel to drive the in-VR live transcript panel AND the score/
///     aura color at that faster cadence, rather than leaving them frozen
///     for a whole coaching cycle. coach_feedback still re-applies score/
///     emotion when it eventually arrives (harmless - same values, just a
///     little stale) and remains the only source of the spoken coaching
///     line/audio.
///   - POST /session/complete : call at the end of a speech. final_transcript
///     is deliberately left blank - the backend falls back to its own
///     server-side accumulated transcript for the session, which is more
///     authoritative than anything Unity could reconstruct from partial
///     telemetry frames. Response feeds UIManager.ShowSessionReport(...).
///
/// Transport note: uses System.Net.WebSockets.ClientWebSocket and
/// System.Net.Http.HttpClient - no third-party networking package was present
/// in this project's manifest. That works fine in the Editor and most
/// standalone builds; behavior under Quest/Android IL2CPP can be inconsistent
/// across Unity versions. If it proves unreliable on-device, swap the
/// transport for a native plugin (e.g. NativeWebSocket) - all the
/// send/receive/dispatch logic here is written to not otherwise care how
/// bytes get on the wire.
/// </summary>
[RequireComponent(typeof(AudioManager))]
public class HubClient : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("Scheme + host + port only, no path (e.g. ws://127.0.0.1:8000 for local dev, or wss://<your-deployed-host> once the hub is hosted). The /session/{id} path and client_type query are appended automatically.")]
    [SerializeField] private string hubBaseUrl = "ws://127.0.0.1:8000";
    [Tooltip("Matches the backend's SessionState id. Kept as 'live_001' by default so it lines up with the mock-mode fallback data baked into GET /session/{id}.")]
    [SerializeField] private string sessionId = "live_001";
    [Tooltip("Matches the backend's default speaker id (SessionCompleteRequest.user_id / User.id).")]
    [SerializeField] private string userId = "user_001";
    [SerializeField] private string sessionTopic = "VR Practice Session";
    [SerializeField] private string sessionLanguage = "en-IN";
    [Tooltip("Matches AudienceManager's fixed 5x6 grid (30 seats) by default.")]
    [SerializeField] private string sessionAudienceSize = "30";
    [Tooltip("Optional bearer token. The current backend has no auth check, so this is unused today - kept only so a future auth layer doesn't require touching this script. Never hard-code a real key here.")]
    [SerializeField] private string authToken;
    [Tooltip("Optional override for the POST /session/complete base URL (scheme+host+port, no path). Leave blank to derive it from hubBaseUrl (ws->http, wss->https), which is how the real single-port FastAPI hub works. Only needed if a test/dev setup splits WS and HTTP across two ports.")]
    [SerializeField] private string httpBaseUrlOverride;

    [Header("Reconnection")]
    [SerializeField] private float reconnectDelaySeconds = 3f;
    [Tooltip("0 = retry forever.")]
    [SerializeField] private int maxReconnectAttempts = 0;
    [Tooltip("Idle keepalive: sends {\"type\":\"ping\"} on the vr socket if nothing else has been sent for this long.")]
    [SerializeField] private float pingIntervalSeconds = 15f;

    [Header("Dependencies")]
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudienceManager audienceManager;
    [SerializeField] private AudioSource ttsAudioSource;

    private ClientWebSocket _vrSocket;
    private ClientWebSocket _telemetrySocket;
    private CancellationTokenSource _lifetimeCts;
    private HttpClient _httpClient;
    private volatile bool _isShuttingDown;
    private int _vrReconnectAttempts;
    private int _telemetryReconnectAttempts;
    private float _lastSendRealtime;

    // Inbound messages are handled on background receive loops; queue the
    // resulting work back onto the main thread since UIManager/AudienceManager
    // touch Unity objects that can only be touched from there.
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    public bool IsSessionComplete { get; private set; }

    private void Awake()
    {
        if (audioManager == null)
        {
            audioManager = GetComponent<AudioManager>();
        }
    }

    private void Start()
    {
        if (audioManager == null || !audioManager.StartRecording())
        {
            Debug.LogError("[HubClient] Could not start microphone recording - voice pipeline will not run.");
            return;
        }

        _httpClient = new HttpClient();
        _lifetimeCts = new CancellationTokenSource();
        _ = RunVrChannelAsync(_lifetimeCts.Token);
        _ = RunTelemetryChannelAsync(_lifetimeCts.Token);
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out Action action))
        {
            action();
        }
    }

    private string BuildWsUrl(string clientType)
    {
        string baseUrl = hubBaseUrl.TrimEnd('/');
        return $"{baseUrl}/session/{Uri.EscapeDataString(sessionId)}?client_type={clientType}";
    }

    private string BuildHttpBaseUrl()
    {
        if (!string.IsNullOrEmpty(httpBaseUrlOverride))
        {
            return httpBaseUrlOverride.TrimEnd('/');
        }

        string baseUrl = hubBaseUrl.TrimEnd('/');
        if (baseUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + baseUrl.Substring("wss://".Length);
        }
        if (baseUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + baseUrl.Substring("ws://".Length);
        }
        return baseUrl;
    }

    // -------------------------------------------------------------------
    // VR channel: sends mic audio, receives coach_feedback + TTS audio.
    // -------------------------------------------------------------------
    private async Task RunVrChannelAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested && !_isShuttingDown)
        {
            _vrSocket?.Dispose();
            _vrSocket = new ClientWebSocket();
            ApplyAuthHeader(_vrSocket);

            bool connected = await TryConnectAsync(_vrSocket, BuildWsUrl("vr"), lifetimeToken, "vr");
            if (connected)
            {
                _vrReconnectAttempts = 0;
                using CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

                Task receiveTask = VrReceiveLoopAsync(_vrSocket, connectionCts.Token);
                Task sendTask = VrSendLoopAsync(_vrSocket, connectionCts.Token);

                await Task.WhenAny(receiveTask, sendTask);
                connectionCts.Cancel();
                await SafeWhenAll(receiveTask, sendTask);
            }

            if (_isShuttingDown || lifetimeToken.IsCancellationRequested)
            {
                break;
            }

            _vrReconnectAttempts++;
            if (maxReconnectAttempts > 0 && _vrReconnectAttempts > maxReconnectAttempts)
            {
                Debug.LogError($"[HubClient] Giving up on vr channel after {_vrReconnectAttempts - 1} reconnect attempt(s).");
                break;
            }

            Debug.LogWarning($"[HubClient] vr channel disconnected. Reconnecting in {reconnectDelaySeconds:0.#}s (attempt {_vrReconnectAttempts})...");
            if (!await DelaySafely(reconnectDelaySeconds, lifetimeToken))
            {
                break;
            }
        }
    }

    private async Task<bool> TryConnectAsync(ClientWebSocket socket, string url, CancellationToken ct, string label)
    {
        try
        {
            Debug.Log($"[HubClient] Connecting {label} channel to {url}...");
            await socket.ConnectAsync(new Uri(url), ct);
            Debug.Log($"[HubClient] {label} channel connected.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HubClient] {label} channel connect failed: {ex.Message}");
            return false;
        }
    }

    private void ApplyAuthHeader(ClientWebSocket socket)
    {
        if (!string.IsNullOrEmpty(authToken))
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {authToken}");
        }
    }

    /// <summary>Drains AudioManager's chunk queue and sends each chunk as a binary frame, plus an idle-keepalive ping. ClientWebSocket allows only one outstanding SendAsync, so both share this single loop.</summary>
    private async Task VrSendLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            bool sentAny = false;

            while (audioManager.TryDequeueChunk(out short[] chunk))
            {
                byte[] bytes = AudioManager.ChunkToBytes(chunk);
                try
                {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, ct);
                    _lastSendRealtime = Time.realtimeSinceStartup;
                    sentAny = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HubClient] Failed to send audio chunk: {ex.Message}");
                    return; // let the outer reconnect loop take over
                }
            }

            if (Time.realtimeSinceStartup - _lastSendRealtime > pingIntervalSeconds)
            {
                try
                {
                    string ping = JsonConvert.SerializeObject(new TypeOnlyMessage { type = "ping" });
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(ping)), WebSocketMessageType.Text, true, ct);
                    _lastSendRealtime = Time.realtimeSinceStartup;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HubClient] Failed to send keepalive ping: {ex.Message}");
                    return;
                }
            }

            // Chunks arrive every 200ms; a short poll interval keeps latency
            // low without busy-looping when the queue is empty.
            if (!await DelaySafely(sentAny ? 0.02f : 0.05f, ct))
            {
                return;
            }
        }
    }

    private async Task VrReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                (WebSocketMessageType type, byte[] messageBytes, bool closed) = await ReceiveFullMessageAsync(socket, buffer, ct);
                if (closed)
                {
                    return;
                }

                if (type == WebSocketMessageType.Text)
                {
                    HandleVrTextMessage(Encoding.UTF8.GetString(messageBytes));
                }
                else
                {
                    Debug.Log($"[HubClient] TTS audio received ({messageBytes.Length} bytes).");
                    _mainThreadActions.Enqueue(() => PlayTtsAudio(messageBytes));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown/reconnect.
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HubClient] vr receive loop error: {ex.Message}");
        }
    }

    private void HandleVrTextMessage(string json)
    {
        Debug.Log($"[HubClient] vr message received: {json}");

        TypeOnlyMessage envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<TypeOnlyMessage>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HubClient] Failed to parse vr message envelope: {ex.Message}");
            return;
        }

        if (envelope == null || string.IsNullOrEmpty(envelope.type))
        {
            return;
        }

        switch (envelope.type)
        {
            case "coach_feedback":
                CoachFeedbackMessage feedback;
                try
                {
                    feedback = JsonConvert.DeserializeObject<CoachFeedbackMessage>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HubClient] Failed to parse coach_feedback: {ex.Message}");
                    return;
                }
                _mainThreadActions.Enqueue(() => DispatchCoachFeedback(feedback));
                break;

            case "pong":
                // Keepalive acknowledgement only - nothing to do.
                break;

            default:
                Debug.LogWarning($"[HubClient] Unknown vr message type '{envelope.type}'.");
                break;
        }
    }

    /// <summary>
    /// The backend only ever sends score+emotion+coaching_text together, so
    /// there is no separate "emotion changed" vs "score changed" event to
    /// react to independently - every field updates in lockstep here.
    /// </summary>
    private void DispatchCoachFeedback(CoachFeedbackMessage feedback)
    {
        if (feedback == null)
        {
            return;
        }

        float scoreNormalized = Mathf.Clamp01(feedback.score / 100f);

        // The live channel never carries a confidence value (only the
        // pull-based GET /emotion does) - normalized score is the closest
        // available proxy for "how sure are we about this read" until the
        // backend streams a real confidence number.
        uiManager?.UpdateScore(feedback.score);
        uiManager?.UpdateEmotion(feedback.emotion, scoreNormalized);

        if (audienceManager != null)
        {
            AudienceEmotion audienceEmotion = MapSpeakerEmotionToAudienceReaction(feedback.emotion);
            audienceManager.UpdateAudience(audienceEmotion, scoreNormalized);
        }

        if (!string.IsNullOrEmpty(feedback.coaching_text))
        {
            uiManager?.ShowCoaching(feedback.coaching_text);
        }
    }

    /// <summary>
    /// The backend has no concept of "audience reaction" distinct from the
    /// speaker's own detected emotion (confirmed against hub.py and the
    /// project roadmap, which explicitly calls for the NPC audience to be
    /// "driven by the live emotion label"). This is the mapping from the
    /// fixed 6-label speaker vocabulary to the 6 audience Animator states.
    /// </summary>
    private static AudienceEmotion MapSpeakerEmotionToAudienceReaction(string speakerEmotion)
    {
        switch (speakerEmotion?.Trim().ToLowerInvariant())
        {
            case "confident": return AudienceEmotion.Engaged;
            case "excited": return AudienceEmotion.Clapping;
            case "nervous": return AudienceEmotion.Sympathetic;
            case "bored": return AudienceEmotion.Bored;
            case "monotone": return AudienceEmotion.Distracted;
            case "calm":
            default: return AudienceEmotion.Neutral;
        }
    }

    /// <summary>Assumes the bytes are a Bulbul-style WAV file (RIFF/WAVE), matching sarvam_service.synthesize_speech's real and mock output. Falls back to treating the bytes as headerless PCM16 mono at AudioManager.SampleRate if no RIFF header is found.</summary>
    private void PlayTtsAudio(byte[] audioBytes)
    {
        if (ttsAudioSource == null)
        {
            Debug.LogWarning("[HubClient] No ttsAudioSource assigned - cannot play TTS audio.");
            return;
        }

        if (!TryDecodeWav(audioBytes, out float[] samples, out int sampleRate, out int channels))
        {
            sampleRate = AudioManager.SampleRate;
            channels = 1;
            samples = DecodeRawPcm16(audioBytes);
        }

        if (samples == null || samples.Length == 0)
        {
            return;
        }

        AudioClip clip = AudioClip.Create("TTSResponse", samples.Length / Mathf.Max(1, channels), Mathf.Max(1, channels), sampleRate, false);
        clip.SetData(samples, 0);

        ttsAudioSource.Stop();
        ttsAudioSource.clip = clip;
        ttsAudioSource.Play();
    }

    private static float[] DecodeRawPcm16(byte[] pcm16Bytes)
    {
        int sampleCount = pcm16Bytes.Length / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(pcm16Bytes[i * 2] | (pcm16Bytes[i * 2 + 1] << 8));
            samples[i] = sample / (float)short.MaxValue;
        }
        return samples;
    }

    /// <summary>Minimal canonical-WAV (PCM16) parser: walks RIFF sub-chunks to find "fmt " (sample rate/channels/bit depth) and "data" (the actual samples), rather than assuming a fixed 44-byte header.</summary>
    private static bool TryDecodeWav(byte[] bytes, out float[] samples, out int sampleRate, out int channels)
    {
        samples = null;
        sampleRate = AudioManager.SampleRate;
        channels = 1;

        if (bytes == null || bytes.Length < 12 ||
            bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
            bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
        {
            return false;
        }

        int bitsPerSample = 16;
        int pos = 12;
        byte[] dataBytes = null;

        while (pos + 8 <= bytes.Length)
        {
            string chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
            int chunkSize = BitConverter.ToInt32(bytes, pos + 4);
            int chunkDataStart = pos + 8;

            if (chunkDataStart + chunkSize > bytes.Length)
            {
                chunkSize = bytes.Length - chunkDataStart; // tolerate a truncated final chunk
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                channels = BitConverter.ToInt16(bytes, chunkDataStart + 2);
                sampleRate = BitConverter.ToInt32(bytes, chunkDataStart + 4);
                bitsPerSample = BitConverter.ToInt16(bytes, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                dataBytes = new byte[chunkSize];
                Array.Copy(bytes, chunkDataStart, dataBytes, 0, chunkSize);
            }

            pos = chunkDataStart + chunkSize + (chunkSize % 2); // chunks are word-aligned
        }

        if (dataBytes == null)
        {
            return false;
        }

        channels = Mathf.Max(1, channels);

        if (bitsPerSample == 16)
        {
            samples = DecodeRawPcm16(dataBytes);
        }
        else if (bitsPerSample == 8)
        {
            samples = new float[dataBytes.Length];
            for (int i = 0; i < dataBytes.Length; i++)
            {
                samples[i] = (dataBytes[i] - 128) / 128f;
            }
        }
        else
        {
            Debug.LogWarning($"[HubClient] Unsupported WAV bit depth ({bitsPerSample}); dropping TTS audio.");
            return false;
        }

        return true;
    }

    private static async Task<(WebSocketMessageType type, byte[] bytes, bool closed)> ReceiveFullMessageAsync(ClientWebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var messageStream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (result.MessageType, Array.Empty<byte>(), true);
            }
            messageStream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (result.MessageType, messageStream.ToArray(), false);
    }

    // -------------------------------------------------------------------
    // Telemetry channel: receive-only, purely to get transcript_partial for
    // the in-VR live transcript panel (the vr channel never carries one).
    // -------------------------------------------------------------------
    private async Task RunTelemetryChannelAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested && !_isShuttingDown)
        {
            _telemetrySocket?.Dispose();
            _telemetrySocket = new ClientWebSocket();
            ApplyAuthHeader(_telemetrySocket);

            bool connected = await TryConnectAsync(_telemetrySocket, BuildWsUrl("app"), lifetimeToken, "telemetry");
            if (connected)
            {
                _telemetryReconnectAttempts = 0;
                try
                {
                    await TelemetryReceiveLoopAsync(_telemetrySocket, lifetimeToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            if (_isShuttingDown || lifetimeToken.IsCancellationRequested)
            {
                break;
            }

            _telemetryReconnectAttempts++;
            if (maxReconnectAttempts > 0 && _telemetryReconnectAttempts > maxReconnectAttempts)
            {
                break;
            }

            if (!await DelaySafely(reconnectDelaySeconds, lifetimeToken))
            {
                break;
            }
        }
    }

    private async Task TelemetryReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            (WebSocketMessageType type, byte[] messageBytes, bool closed) = await ReceiveFullMessageAsync(socket, buffer, ct);
            if (closed || type != WebSocketMessageType.Text)
            {
                if (closed) return;
                continue;
            }

            LiveUpdateMessage update;
            try
            {
                update = JsonConvert.DeserializeObject<LiveUpdateMessage>(Encoding.UTF8.GetString(messageBytes));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HubClient] Failed to parse telemetry frame: {ex.Message}");
                continue;
            }

            if (update != null)
            {
                _mainThreadActions.Enqueue(() => DispatchLiveUpdate(update));
            }
        }
    }

    /// <summary>
    /// Drives the live transcript panel, % score, emotion aura color, and
    /// audience reaction from the fast ~PIPELINE_WINDOW_SEC telemetry
    /// cadence instead of waiting on coach_feedback's much slower full
    /// LLM+TTS round-trip - see the class doc comment above for why.
    /// </summary>
    private void DispatchLiveUpdate(LiveUpdateMessage update)
    {
        uiManager?.UpdateLiveTranscript(update.transcript_partial);

        float scoreNormalized = Mathf.Clamp01(update.score / 100f);
        uiManager?.UpdateScore(update.score);
        uiManager?.UpdateEmotion(update.emotion_label, scoreNormalized);

        if (audienceManager != null)
        {
            AudienceEmotion audienceEmotion = MapSpeakerEmotionToAudienceReaction(update.emotion_label);
            audienceManager.UpdateAudience(audienceEmotion, scoreNormalized);
        }
    }

    // -------------------------------------------------------------------
    // Session completion: POST /session/complete, then show the report.
    // -------------------------------------------------------------------
    /// <summary>Call this when the speaker finishes (e.g. from a HUD "Finish Speech" button or a session timer expiring).</summary>
    public void EndSession()
    {
        if (IsSessionComplete)
        {
            return;
        }
        IsSessionComplete = true;

        audioManager?.StopRecording();
        _ = CompleteSessionAsync();
    }

    private async Task CompleteSessionAsync()
    {
        var request = new SessionCompleteRequestData
        {
            session_id = sessionId,
            user_id = userId,
            topic = sessionTopic,
            language = sessionLanguage,
            audience_size = sessionAudienceSize,
            // final_transcript intentionally left null: the backend falls
            // back to its own server-side accumulated transcript for this
            // session, which is more complete than anything reconstructable
            // here from partial telemetry frames.
        };

        string url = $"{BuildHttpBaseUrl()}/session/complete";
        string body = JsonConvert.SerializeObject(request);

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            // ConfigureAwait(false): EndSession() (and therefore this) can be
            // invoked from outside Unity's normal main-thread call chain (a
            // UI event, an editor/test bridge, etc.) - the continuation here
            // never touches a Unity API directly (ShowSessionReport is
            // correctly deferred via _mainThreadActions below), so it must
            // not depend on capturing whatever SynchronizationContext happened
            // to be current when this was called.
            HttpResponseMessage response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Debug.LogError($"[HubClient] /session/complete failed: {(int)response.StatusCode} {responseJson}");
                return;
            }

            SessionCompleteResponseData result = JsonConvert.DeserializeObject<SessionCompleteResponseData>(responseJson);
            _mainThreadActions.Enqueue(() => uiManager?.ShowSessionReport(result?.session));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HubClient] /session/complete request error: {ex.Message}");
        }
    }

    private static async Task<bool> DelaySafely(float seconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task SafeWhenAll(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Cancellation-related faults are expected here; the outer loop
            // already decided this connection is done.
        }
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }
        _isShuttingDown = true;

        audioManager?.StopRecording();
        _lifetimeCts?.Cancel();

        CloseSocketQuietly(_vrSocket);
        CloseSocketQuietly(_telemetrySocket);
        _httpClient?.Dispose();

        Debug.Log("[HubClient] Shutdown complete.");
    }

    private static void CloseSocketQuietly(ClientWebSocket socket)
    {
        try
        {
            socket?.Abort();
            socket?.Dispose();
        }
        catch
        {
            // Best-effort cleanup; ignore failures during shutdown.
        }
    }
}
