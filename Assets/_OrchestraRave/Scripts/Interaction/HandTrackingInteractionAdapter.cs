
using UnityEngine;

public class HandTrackingInteractionAdapter : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private OrchestraInteractionManager interactionManager;

    [SerializeField]
    private InstrumentSelector instrumentSelector;

    [Header("Settings")]
    [SerializeField]
    private bool enableGlobalVolume = true;

    [SerializeField]
    private bool enableGlobalFilter = true;

    [SerializeField]
    private bool enableGroupVolume = true;

    // --------------------------------------------------
    // LEFT HAND : GLOBAL CONTROLS
    // --------------------------------------------------

    /// <summary>
    /// Appeler lorsque les données de la main gauche
    /// sont disponibles.
    ///
    /// Les valeurs doivent être normalisées entre 0 et 1.
    /// </summary>
    public void SubmitLeftHand(
        bool isTracked,
        bool isOpen,
        float height01,
        float wristRotation01)
    {
        if (interactionManager == null)
            return;

        if (!isTracked || !isOpen)
            return;

        if (enableGlobalVolume)
        {
            interactionManager.SetGlobalVolume(height01);
        }

        if (enableGlobalFilter)
        {
            interactionManager.SetGlobalFilter(wristRotation01);
        }
    }

    // --------------------------------------------------
    // RIGHT HAND : POINTING
    // --------------------------------------------------

    /// <summary>
    /// Transmet le pointage de la main droite.
    /// À appeler à chaque frame de suivi valide.
    /// </summary>
    public void SubmitRightHandPointing(
        bool isTracked,
        Ray pointingRay,
        bool isFistClosed)
    {
        if (instrumentSelector == null)
            return;

        if (!isTracked || isFistClosed)
        {
            instrumentSelector.StopPointing();
            return;
        }

        instrumentSelector.UpdatePointing(pointingRay);
    }

    // --------------------------------------------------
    // RIGHT HAND : GROUP VOLUME
    // --------------------------------------------------

    /// <summary>
    /// Contrôle le volume des groupes sélectionnés.
    /// La valeur doit être normalisée entre 0 et 1.
    /// </summary>
    public void SubmitRightHandVolume(
        bool isTracked,
        bool isFistClosed,
        float height01)
    {
        if (interactionManager == null ||
            !enableGroupVolume)
            return;

        if (!isTracked || !isFistClosed)
            return;

        interactionManager.SetSelectedGroupsVolume(height01);
    }

    // --------------------------------------------------
    // MUTE
    // --------------------------------------------------

    /// <summary>
    /// À appeler UNE SEULE FOIS lorsque le geste de mute
    /// est reconnu, jamais à chaque frame.
    /// </summary>
    public void TriggerSelectedGroupsMute()
    {
        if (interactionManager == null)
            return;

        interactionManager.ToggleSelectedGroupsMute();
    }

    // --------------------------------------------------
    // TRACKING LOST
    // --------------------------------------------------

    /// <summary>
    /// À appeler lorsque la main droite n'est plus suivie.
    /// </summary>
    public void NotifyRightHandLost()
    {
        if (instrumentSelector != null)
            instrumentSelector.StopPointing();
    }

    private void OnDisable()
    {
        NotifyRightHandLost();
    }
}
