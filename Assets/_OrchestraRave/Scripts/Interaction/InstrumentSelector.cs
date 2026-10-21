using UnityEngine;

public class InstrumentSelector : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField]
    private float maxSelectionDistance = 15f;

    [SerializeField]
    private LayerMask instrumentLayer;

    [SerializeField]
    private OrchestraInteractionManager interactionManager;

    [Header("Automatic Selection")]
    [SerializeField, Min(0.1f)]
    private float confirmationDuration = 1f;

    [SerializeField, Min(0f)]
    private float interruptionTolerance = 0.2f;

    private InstrumentGroupId hoveredGroup = InstrumentGroupId.None;
    private InstrumentGroupId candidateGroup = InstrumentGroupId.None;

    private float confirmationProgress = 0f;
    private float interruptionTime = 0f;
    private bool selectionArmed = true;

    public void UpdatePointing(Ray p_ray)
    {
        InstrumentGroupId detectedGroup = DetectGroup(p_ray);

        hoveredGroup = detectedGroup;

        UpdateSelection(detectedGroup, Time.deltaTime);
    }

    private InstrumentGroupId DetectGroup(Ray p_ray)
    {
        if (!Physics.Raycast(
            p_ray,
            out RaycastHit hit,
            maxSelectionDistance,
            instrumentLayer))
        {
            return InstrumentGroupId.None;
        }

        InstrumentTarget target =
            hit.collider.GetComponentInParent<InstrumentTarget>();

        if (target == null)
        {
            return InstrumentGroupId.None;
        }

        return target.GroupId;
    }

    private void UpdateSelection(
        InstrumentGroupId detectedGroup,
        float deltaTime)
    {
        if (candidateGroup == InstrumentGroupId.None)
        {
            if (detectedGroup != InstrumentGroupId.None)
            {
                StartCandidate(detectedGroup);
            }

            return;
        }

        if (detectedGroup == candidateGroup)
        {
            interruptionTime = 0f;

            if (!selectionArmed)
                return;

            confirmationProgress += deltaTime;

            if (confirmationProgress >= confirmationDuration)
            {
                ConfirmSelection();
            }

            return;
        }

        interruptionTime += deltaTime;

        if (interruptionTime < interruptionTolerance)
            return;
        ResetCandidate();

        if (detectedGroup != InstrumentGroupId.None)
        {
            StartCandidate(detectedGroup);
        }
    }

    private void StartCandidate(InstrumentGroupId group)
    {
        candidateGroup = group;

        confirmationProgress = 0f;
        interruptionTime = 0f;

        selectionArmed = true;
    }

    private void ConfirmSelection()
    {
        if (interactionManager == null)
            return;

        interactionManager.ToggleGroupSelection(candidateGroup);
        selectionArmed = false;

        Debug.Log($"Selection toggled: {candidateGroup}");
    }

    private void ResetCandidate()
    {
        candidateGroup = InstrumentGroupId.None;

        confirmationProgress = 0f;
        interruptionTime = 0f;

        selectionArmed = true;
    }

    private void OnDisable()
    {
        ResetCandidate();
        hoveredGroup = InstrumentGroupId.None;
    }
}