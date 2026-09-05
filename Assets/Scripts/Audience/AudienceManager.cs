using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a grid of AudienceMember instances in front of the stage and
/// broadcasts emotion/engagement updates to all of them. This is the single
/// entry point other systems (speech analysis, timer, etc.) should call to
/// react the crowd.
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

    [Header("Test (Inspector)")]
    [SerializeField] private AudienceEmotion testEmotion = AudienceEmotion.Engaged;
    [SerializeField, Range(0f, 1f)] private float testEngagementScore = 0.75f;

    private readonly List<AudienceMember> _members = new List<AudienceMember>();

    public int MemberCount => _members.Count;

    private void Start()
    {
        if (_members.Count == 0)
        {
            SpawnAudience();
        }
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
    /// Public entry point for other systems to react the whole audience to a
    /// given emotion at a given engagement score (0-1).
    /// </summary>
    public void UpdateAudience(AudienceEmotion emotion, float engagementScore)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i] != null)
            {
                _members[i].SetEmotion(emotion, engagementScore);
            }
        }
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
    }
}
