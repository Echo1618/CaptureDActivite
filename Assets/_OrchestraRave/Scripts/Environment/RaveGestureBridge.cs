
using UnityEngine;
using Maestro.Environment;

public class RaveGestureBridge : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField]
    private OrchestraInteractionManager interactionManager;

    private bool leftShaka;
    private bool rightShaka;
    private bool requestSent;

    private void OnEnable()
    {
        MaestroOrchestraApi.OnRavePhaseChanged += OnRavePhaseChanged;
    }

    private void OnDisable()
    {
        // Annuler uniquement si le compte à rebours est en cours.
        if (MaestroOrchestraApi.RavePhase == RaveSequence.PhaseCountdown)
            MaestroOrchestraApi.AbortRaveSequence();

        MaestroOrchestraApi.OnRavePhaseChanged -= OnRavePhaseChanged;

        leftShaka = false;
        rightShaka = false;
        requestSent = false;
    }

    public void OnLeftShakaStarted()
    {
        leftShaka = true;
        Debug.Log("[SHAKA] Gauche START");
        UpdateRequest();
    }

    public void OnLeftShakaEnded()
    {
        leftShaka = false;
        Debug.Log("[SHAKA] Gauche END");
        UpdateRequest();
    }

    public void OnRightShakaStarted()
    {
        rightShaka = true;
        Debug.Log("[SHAKA] Droit START");
        UpdateRequest();
    }

    public void OnRightShakaEnded()
    {
        rightShaka = false;
        Debug.Log("[SHAKA] Droit END");
        UpdateRequest();
    }

    private void UpdateRequest()
    {
        bool bothActive = leftShaka && rightShaka;

        if (bothActive && !requestSent)
        {
            requestSent = true;

            if (!MaestroOrchestraApi.IsRaveSequenceRunning)
            {
                Debug.Log("[SHAKA] DEUX MAINS OK - DEMANDE RAVE");
                MaestroOrchestraApi.StartRaveSequence();
                Debug.Log("[RAVE] " + MaestroOrchestraApi.Diagnose());
                Debug.Log("[RAVE] Running = " + MaestroOrchestraApi.IsRaveSequenceRunning);
                Debug.Log("[RAVE] Phase = " + MaestroOrchestraApi.RavePhase);
            }
        }
        else if (!bothActive && requestSent)
        {
            requestSent = false;

            // Le geste doit rester actif durant le countdown.
            // Après la validation, la séquence est autonome.
            if (MaestroOrchestraApi.RavePhase ==
                RaveSequence.PhaseCountdown)
            {
                MaestroOrchestraApi.AbortRaveSequence();
            }
        }
    }

    private void OnRavePhaseChanged(string phase)
    {
        if (interactionManager == null)
            return;

        if (phase == RaveSequence.PhaseParty)
        {
            interactionManager.ChangePhase(MusicPhase.Techno);
        }
        else if (phase == RaveSequence.PhaseIdle &&
                 interactionManager.CurrentPhase != MusicPhase.Classical)
        {
            interactionManager.ChangePhase(MusicPhase.Classical);
        }
    }
}
