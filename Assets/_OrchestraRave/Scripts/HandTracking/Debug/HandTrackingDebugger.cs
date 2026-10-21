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

        timer += Time.deltaTime;

        if (timer < LogInterval)
            return;

        timer = 0f;

        Debug.Log(
            $"Main gauche suivie : {handTrackingManager.IsLeftHandTracked} | " +
            $"Poignet gauche : {handTrackingManager.LeftHand.WristPosition} | " +
            $"Main droite suivie : {handTrackingManager.IsRightHandTracked} | " +
            $"Poignet droit : {handTrackingManager.RightHand.WristPosition}"
        );
    }
}
