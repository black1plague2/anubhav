using UnityEngine;

/// <summary>
/// Emotional/engagement states an audience member can react with.
/// Maps 1:1 to the six Animator triggers on the Audience Animator Controller.
/// </summary>
public enum AudienceEmotion
{
    Neutral,
    Engaged,
    Sympathetic,
    Bored,
    Clapping,
    Distracted
}

/// <summary>
/// Drives a single audience member's Animator based on emotion/engagement
/// updates pushed down from AudienceManager.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AudienceMember : MonoBehaviour
{
    private static readonly int IsEngagedHash = Animator.StringToHash("isEngaged");
    private static readonly int IsSympatheticHash = Animator.StringToHash("isSympathetic");
    private static readonly int IsBoredHash = Animator.StringToHash("isBored");
    private static readonly int IsClappingHash = Animator.StringToHash("isClapping");
    private static readonly int IsDistractedHash = Animator.StringToHash("isDistracted");
    private static readonly int IsNeutralHash = Animator.StringToHash("isNeutral");

    [SerializeField] private Animator animator;

    public AudienceEmotion CurrentEmotion { get; private set; } = AudienceEmotion.Neutral;
    public float EngagementScore { get; private set; }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    /// <summary>
    /// Applies an emotional reaction to this audience member.
    /// engagementScore (0-1) is stored for systems that want to read it back
    /// (e.g. to drive animation speed/blend weight later) but does not itself
    /// pick the trigger - the emotion argument does.
    /// </summary>
    public void SetEmotion(AudienceEmotion emotion, float engagementScore)
    {
        CurrentEmotion = emotion;
        EngagementScore = Mathf.Clamp01(engagementScore);

        if (animator == null)
        {
            return;
        }

        switch (emotion)
        {
            case AudienceEmotion.Engaged:
                animator.SetTrigger(IsEngagedHash);
                break;
            case AudienceEmotion.Sympathetic:
                animator.SetTrigger(IsSympatheticHash);
                break;
            case AudienceEmotion.Bored:
                animator.SetTrigger(IsBoredHash);
                break;
            case AudienceEmotion.Clapping:
                animator.SetTrigger(IsClappingHash);
                break;
            case AudienceEmotion.Distracted:
                animator.SetTrigger(IsDistractedHash);
                break;
            case AudienceEmotion.Neutral:
            default:
                animator.SetTrigger(IsNeutralHash);
                break;
        }
    }
}
