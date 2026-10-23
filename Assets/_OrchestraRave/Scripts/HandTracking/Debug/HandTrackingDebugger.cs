
using UnityEngine;

public class HandTrackingDebugger : MonoBehaviour
{
    [SerializeField] private HandTrackingManager handTrackingManager;

    private float timer;
    private const float LogInterval = 1f;

    private void Update()
    {
        if (handTrackingManager == null)
            return;

        // Vérifier le début du pincement à chaque frame.
        if (handTrackingManager.RightHand.PinchStarted)
        {
            Debug.Log("PINCH STARTED - Main droite");
        }

        // Les informations générales restent affichées une fois par seconde.
        timer += Time.deltaTime;

        if (timer < LogInterval)
            return;

        timer = 0f;

        Debug.Log(
            $"Main gauche suivie : {handTrackingManager.IsLeftHandTracked} | " +
            $"Poignet gauche : {handTrackingManager.LeftHand.WristPosition} | " +
            $"Main droite suivie : {handTrackingManager.IsRightHandTracked} | " +
            $"Poignet droit : {handTrackingManager.RightHand.WristPosition} | " +
            $"Main gauche ouverte : {handTrackingManager.LeftHand.IsOpen} | " +
            $"Main droite en pointage : {handTrackingManager.RightHand.IsPointing} | " +
            $"Rayon : {handTrackingManager.RightHand.PointingRayDirection}"
        );
    }
}
