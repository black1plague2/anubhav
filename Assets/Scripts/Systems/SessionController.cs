using UnityEngine;

/// <summary>
/// Drives the session clock and provides the single entry point for ending a
/// practice speech. Nothing else in the project was calling
/// UIManager.UpdateTimer or HubClient.EndSession before this existed - audio
/// streaming and the score/emotion/coaching loop ran indefinitely with no way
/// to close out a session and see the Coaching Report panel.
/// </summary>
public class SessionController : MonoBehaviour
{
    [SerializeField] private UIManager uiManager;
    [SerializeField] private HubClient hubClient;

    [Tooltip("0 = count up indefinitely with no automatic end (call EndSession() manually, e.g. from a HUD button). >0 = counts down from this many seconds and auto-ends the session at zero.")]
    [SerializeField] private float sessionDurationSeconds = 0f;

    private float _elapsedSeconds;
    private bool _ended;

    private void Update()
    {
        if (_ended)
        {
            return;
        }

        _elapsedSeconds += Time.deltaTime;

        if (sessionDurationSeconds > 0f)
        {
            float remaining = Mathf.Max(0f, sessionDurationSeconds - _elapsedSeconds);
            uiManager?.UpdateTimer(remaining);
            if (remaining <= 0f)
            {
                EndSession();
            }
        }
        else
        {
            uiManager?.UpdateTimer(_elapsedSeconds);
        }
    }

    /// <summary>Wire this to a HUD "Finish Speech" button or a controller binding.</summary>
    public void EndSession()
    {
        if (_ended)
        {
            return;
        }
        _ended = true;

        uiManager?.HideCoaching();
        uiManager?.HideLiveTranscript();
        hubClient?.EndSession();
    }

    [ContextMenu("Test/End Session Now")]
    private void TestEndSessionNow()
    {
        EndSession();
    }
}
