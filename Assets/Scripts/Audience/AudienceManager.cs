using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a grid of AudienceMember instances in front of the stage. This is
/// the single entry point other systems (speech analysis, timer, etc.)
/// should call to react the crowd.
///
/// UpdateAudience does NOT react everyone at once - a real crowd doesn't
/// move in unison, and 30 members playing the same gesture simultaneously
/// reads as obviously synthetic. Instead it just records the current
/// emotion/score, and an internal timer (see ReactionLoop) picks ONE random
/// member every few seconds to actually play that reaction, reverting
/// whoever reacted previously back to idle first - so at any moment at most
/// one person in the crowd is mid-gesture and everyone else is idle.
/// </summary>
public class AudienceManager : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("One or more audience member prefabs. When more than one is set, seats are filled from a shuffled, evenly-distributed sequence so no single character repeats more than necessary.")]
    [SerializeField] private GameObject[] audienceMemberPrefabs;

    [Header("Grid")]
    [SerializeField] private int rows = 5;
    [SerializeField] private int columns = 6;
    [SerializeField] private float rowSpacing = 1.2f;
    [SerializeField] private float columnSpacing = 1.1f;
    [SerializeField] private Vector3 gridOrigin = new Vector3(0f, 0.4375f, -2.7f);

    [Header("Reaction Timing")]
    [Tooltip("How often (seconds, randomized between these two) a single " +
             "audience member is chosen to react to the current score/emotion. " +
             "Everyone else stays in their idle state - a real crowd doesn't " +
             "move in unison.")]
    [SerializeField] private float minReactionInterval = 6f;
    [SerializeField] private float maxReactionInterval = 7f;

    [Header("Test (Inspector)")]
    [SerializeField] private AudienceEmotion testEmotion = AudienceEmotion.Engaged;
    [SerializeField, Range(0f, 1f)] private float testEngagementScore = 0.75f;

    private readonly List<AudienceMember> _members = new List<AudienceMember>();

    // The crowd's current "mood" as last reported via UpdateAudience - not
    // applied to anyone directly. ReactionLoop reads this on its own timer
    // and applies it to one member at a time.
    private AudienceEmotion _currentEmotion = AudienceEmotion.Neutral;
    private float _currentEngagementScore;
    private AudienceMember _currentlyReactingMember;
    private Coroutine _reactionLoop;

    public int MemberCount => _members.Count;

    private void Start()
    {
        // If the audience already exists in the scene (placed/arranged in
        // the Editor and saved), adopt those children as-is instead of
        // destroying and respawning them - _members is a runtime-only list
        // that starts empty every Play session, so without this check,
        // every single Play would wipe and regenerate the whole crowd at
        // fresh grid positions with a newly-shuffled character order,
        // discarding any manual seat/position tweaks made in the Editor.
        if (transform.childCount > 0)
        {
            _members.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                AudienceMember member = transform.GetChild(i).GetComponent<AudienceMember>();
                if (member != null)
                {
                    _members.Add(member);
                }
            }
        }

        if (_members.Count == 0)
        {
            SpawnAudience();
        }

        _reactionLoop = StartCoroutine(ReactionLoop());
    }

    private void OnDisable()
    {
        if (_reactionLoop != null)
        {
            StopCoroutine(_reactionLoop);
            _reactionLoop = null;
        }
    }

    private IEnumerator ReactionLoop()
    {
        while (true)
        {
            ReactOneMember();
            yield return new WaitForSeconds(Random.Range(minReactionInterval, maxReactionInterval));
        }
    }

    /// <summary>Reverts whoever reacted last back to idle, then makes one newly-picked random member play the crowd's current mood.</summary>
    private void ReactOneMember()
    {
        if (_members.Count == 0)
        {
            return;
        }

        if (_currentlyReactingMember != null)
        {
            _currentlyReactingMember.SetEmotion(AudienceEmotion.Neutral, _currentEngagementScore);
        }

        AudienceMember next = _members[Random.Range(0, _members.Count)];
        next.SetEmotion(_currentEmotion, _currentEngagementScore);
        _currentlyReactingMember = next;
    }

    /// <summary>
    /// Clears any existing audience and instantiates a rows x columns grid
    /// of audienceMemberPrefab in front of the stage, parented to this
    /// GameObject's transform.
    /// </summary>
    public void SpawnAudience()
    {
        // Destroy ALL existing children of this transform rather than only
        // what _members remembers - _members is a runtime-only list and
        // does not survive a Play Mode transition or domain reload, so
        // relying on it here would leave stale children behind and spawn
        // duplicates on top of them.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediateOrRuntime(transform.GetChild(i).gameObject);
        }
        _members.Clear();

        if (audienceMemberPrefabs == null || audienceMemberPrefabs.Length == 0)
        {
            Debug.LogWarning("AudienceManager: audienceMemberPrefabs is empty - cannot spawn audience.", this);
            return;
        }

        float gridWidth = (columns - 1) * columnSpacing;
        int[] prefabOrder = BuildShuffledPrefabOrder(rows * columns);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Vector3 localOffset = new Vector3(
                    -gridWidth * 0.5f + col * columnSpacing,
                    0f,
                    -row * rowSpacing);

                Vector3 spawnPosition = gridOrigin + transform.TransformDirection(localOffset);

                int seatIndex = row * columns + col;
                GameObject prefab = audienceMemberPrefabs[prefabOrder[seatIndex]];

                GameObject instance = Instantiate(prefab, spawnPosition, transform.rotation, transform);
                instance.name = $"AudienceMember_R{row + 1}C{col + 1}";

                AudienceMember member = instance.GetComponent<AudienceMember>();
                if (member == null)
                {
                    member = instance.AddComponent<AudienceMember>();
                }

                _members.Add(member);
            }
        }
    }

    /// <summary>
    /// Builds a seat-count-long sequence of prefab indices by repeatedly
    /// shuffling a full pass through all prefab indices (Fisher-Yates), so
    /// every prefab is used before any repeats within a pass - avoids
    /// clumping the same character in adjacent seats while still covering
    /// seatCount &gt; prefab count.
    /// </summary>
    private int[] BuildShuffledPrefabOrder(int seatCount)
    {
        int prefabCount = audienceMemberPrefabs.Length;
        int[] order = new int[seatCount];
        int filled = 0;

        while (filled < seatCount)
        {
            int[] pass = new int[prefabCount];
            for (int i = 0; i < prefabCount; i++) pass[i] = i;

            for (int i = prefabCount - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pass[i], pass[j]) = (pass[j], pass[i]);
            }

            int copyCount = Mathf.Min(prefabCount, seatCount - filled);
            System.Array.Copy(pass, 0, order, filled, copyCount);
            filled += copyCount;
        }

        return order;
    }

    /// <summary>
    /// Public entry point for other systems to report the speaker's current
    /// emotion/score. This does NOT react the whole crowd immediately - it
    /// just updates what ReactionLoop hands to the next member it picks, on
    /// its own every-6-7-seconds cadence (see class doc).
    /// </summary>
    public void UpdateAudience(AudienceEmotion emotion, float engagementScore)
    {
        _currentEmotion = emotion;
        _currentEngagementScore = engagementScore;
    }

    private static void DestroyImmediateOrRuntime(GameObject go)
    {
        if (Application.isPlaying)
        {
            Destroy(go);
        }
        else
        {
            DestroyImmediate(go);
        }
    }

    [ContextMenu("Test/Update Audience With Inspector Values")]
    private void TestUpdateAudience()
    {
        UpdateAudience(testEmotion, testEngagementScore);
        // Also trigger a reaction immediately rather than waiting for the
        // next timed tick, so this test button still gives instant feedback.
        ReactOneMember();
    }
}
