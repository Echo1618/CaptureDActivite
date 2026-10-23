
using UnityEngine;

public class InstrumentSelector : MonoBehaviour
{
    [Header("Raycast")]

    [SerializeField, Min(0.1f)]
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

    // --------------------------------------------------
    // PROPRIETES DE DEBUG
    // --------------------------------------------------

    public InstrumentGroupId HoveredGroup => hoveredGroup;
    public InstrumentGroupId CandidateGroup => candidateGroup;

    public float ConfirmationProgress01 =>
        confirmationDuration > 0f
            ? Mathf.Clamp01(
                confirmationProgress / confirmationDuration)
            : 0f;

    public float InterruptionTime => interruptionTime;
    public bool SelectionArmed => selectionArmed;

    // --------------------------------------------------
    // POINTAGE
    // --------------------------------------------------

    public void UpdatePointing(Ray p_ray)
    {
        InstrumentGroupId detectedGroup = DetectGroup(p_ray);

        hoveredGroup = detectedGroup;

        UpdateSelection(detectedGroup, Time.deltaTime);
    }

    public void StopPointing()
    {
        hoveredGroup = InstrumentGroupId.None;
        ResetCandidate();
    }

    // --------------------------------------------------
    // RAYCAST
    // --------------------------------------------------

    private InstrumentGroupId DetectGroup(Ray p_ray)
    {
        if (!Physics.Raycast(
            p_ray,
            out RaycastHit hit,
            maxSelectionDistance,
            instrumentLayer,
            QueryTriggerInteraction.Collide))
        {
            return InstrumentGroupId.None;
        }

        InstrumentTarget target =
            hit.collider.GetComponentInParent<InstrumentTarget>();

        if (target == null)
            return InstrumentGroupId.None;

        if (target.GroupId == InstrumentGroupId.None)
            return InstrumentGroupId.None;

        return target.GroupId;
    }

    // --------------------------------------------------
    // SELECTION AUTOMATIQUE
    // --------------------------------------------------

    private void UpdateSelection(
        InstrumentGroupId detectedGroup,
        float deltaTime)
    {
        // Aucun candidat pour l'instant
        if (candidateGroup == InstrumentGroupId.None)
        {
            if (detectedGroup != InstrumentGroupId.None)
            {
                StartCandidate(detectedGroup);
            }

            return;
        }

        // Le joueur continue de pointer le candidat
        if (detectedGroup == candidateGroup)
        {
            interruptionTime = 0f;

            // Déjà confirmé : attendre qu'il quitte la cible
            if (!selectionArmed)
                return;

            confirmationProgress += deltaTime;

            if (confirmationProgress >= confirmationDuration)
            {
                ConfirmSelection();
            }

            return;
        }

        // Le joueur pointe autre chose ou rien
        interruptionTime += deltaTime;

        // On conserve temporairement le candidat
        if (interruptionTime < interruptionTolerance)
            return;

        // L'interruption a duré trop longtemps
        ResetCandidate();

        // Un autre groupe peut devenir candidat
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
        {
            Debug.LogWarning(
                "InstrumentSelector : InteractionManager manquant.",
                this
            );

            // Ne pas répéter l'avertissement chaque frame.
            selectionArmed = false;
            return;
        }

        interactionManager.ToggleGroupSelection(candidateGroup);

        selectionArmed = false;

        Debug.Log(
            $"InstrumentSelector : sélection inversée pour {candidateGroup}"
        );
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
        StopPointing();
    }
}
