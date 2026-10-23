
using UnityEngine;
using UnityEngine.XR.Hands;

public class HandTrackingDiagnostic : MonoBehaviour
{
    [SerializeField]
    private XRHandTrackingEvents leftHand;

    [SerializeField]
    private XRHandTrackingEvents rightHand;

    private float nextLogTime;

    private void Update()
    {
        if (Time.time < nextLogTime)
            return;

        nextLogTime = Time.time + 1f;

        Debug.Log(
            "[HAND DIAGNOSTIC] " +
            $"Left component: {(leftHand != null && leftHand.isActiveAndEnabled)} | " +
            $"Left tracked: {(leftHand != null && leftHand.handIsTracked)} | " +
            $"Right component: {(rightHand != null && rightHand.isActiveAndEnabled)} | " +
            $"Right tracked: {(rightHand != null && rightHand.handIsTracked)}"
        );
    }
}
