
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

public class StaticGestureInteractionBridge : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private InstrumentSelector instrumentSelector;
    [SerializeField] private OrchestraInteractionManager interactionManager;

    [Header("XR")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Camera headCamera;

    [Header("Volume")]
    [SerializeField] private float minHeightBelowHead = -0.65f;
    [SerializeField] private float maxHeightBelowHead = -0.10f;

    [Header("Pointage")]
    [SerializeField] private float rayDebugLength = 10f;
    [SerializeField] private float gestureLossGrace = 0.25f;
    [SerializeField] private bool showDebugRay = true;

    private readonly List<XRHandSubsystem> handSubsystems =
        new List<XRHandSubsystem>();

    private XRHandSubsystem hands;

    // Gestes continus
    private bool fingerPointActive;
    private bool palmUpActive;
    private bool rightPalmUpActive;

    // Gestes ponctuels
    private bool leftFistActive;
    private bool rightFistActive;
    private bool thumbDownActive;

    private float pointInactiveTime;
    private float nextSubsystemSearch;

    // Debug
    public bool FingerPointActive => fingerPointActive;
    public bool PalmUpActive => palmUpActive;
    public bool RightPalmUpActive => rightPalmUpActive;

    public bool LeftFistActive => leftFistActive;
    public bool RightFistActive => rightFistActive;
    public bool ThumbDownActive => thumbDownActive;

    // ==========================================
    // FINGER POINT - MAIN DROITE
    // ==========================================

    public void OnFingerPointStarted()
    {
        fingerPointActive = true;
        pointInactiveTime = 0f;
    }

    public void OnFingerPointEnded()
    {
        fingerPointActive = false;
    }

    // ==========================================
    // PALM UP - MAIN GAUCHE
    // ==========================================

    public void OnPalmUpStarted()
    {
        palmUpActive = true;
    }

    public void OnPalmUpEnded()
    {
        palmUpActive = false;
    }

    // ==========================================
    // PALM UP - MAIN DROITE
    // ==========================================

    public void OnRightPalmUpStarted()
    {
        rightPalmUpActive = true;
        StopPointing();
    }

    public void OnRightPalmUpEnded()
    {
        rightPalmUpActive = false;
    }

    // ==========================================
    // POING GAUCHE - MUTE GLOBAL
    // ==========================================

    public void OnLeftFistStarted()
    {
        if (leftFistActive)
            return;

        leftFistActive = true;

        if (interactionManager != null)
            interactionManager.ToggleGlobalMute();
    }

    public void OnLeftFistEnded()
    {
        leftFistActive = false;
    }

    // ==========================================
    // POING DROIT - MUTE LOCAL
    // ==========================================

    public void OnRightFistStarted()
    {
        if (rightFistActive)
            return;

        rightFistActive = true;
        StopPointing();

        if (interactionManager != null)
            interactionManager.ToggleSelectedGroupsMute();
    }

    public void OnRightFistEnded()
    {
        rightFistActive = false;
    }

    // ==========================================
    // THUMB DOWN DROIT - CLEAR SELECTION
    // ==========================================

    public void OnThumbDownStarted()
    {
        if (thumbDownActive)
            return;

        thumbDownActive = true;
        StopPointing();

        if (interactionManager != null)
            interactionManager.ClearGroupSelection();
    }

    public void OnThumbDownEnded()
    {
        thumbDownActive = false;
    }

    // ==========================================
    // INITIALISATION
    // ==========================================

    private void Start()
    {
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();

        if (headCamera == null)
            headCamera = Camera.main;

        FindHandSubsystem();
    }

    private void FindHandSubsystem()
    {
        handSubsystems.Clear();
        SubsystemManager.GetSubsystems(handSubsystems);

        hands = null;

        foreach (XRHandSubsystem subsystem in handSubsystems)
        {
            if (subsystem != null && subsystem.running)
            {
                hands = subsystem;
                break;
            }
        }
    }

    // ==========================================
    // UPDATE
    // ==========================================

    private void Update()
    {
        if (hands == null || !hands.running)
        {
            if (Time.unscaledTime >= nextSubsystemSearch)
            {
                nextSubsystemSearch = Time.unscaledTime + 1f;
                FindHandSubsystem();
            }

            if (hands == null || !hands.running)
            {
                StopPointing();
                return;
            }
        }

        UpdateFingerPoint();
        UpdatePalmUp();
        UpdateRightPalmUp();
    }

    // ==========================================
    // RAYCAST - INDEX DROIT
    // ==========================================

    private void UpdateFingerPoint()
    {
        if (instrumentSelector == null)
            return;

        // Les gestes d'action bloquent la sélection.
        if (rightPalmUpActive ||
            rightFistActive ||
            thumbDownActive)
        {
            StopPointing();
            return;
        }

        XRHand rightHand = hands.rightHand;

        if (!rightHand.isTracked)
        {
            StopPointing();
            return;
        }

        if (!fingerPointActive)
        {
            pointInactiveTime += Time.deltaTime;

            if (pointInactiveTime >= gestureLossGrace)
                instrumentSelector.StopPointing();

            return;
        }

        pointInactiveTime = 0f;

        XRHandJoint tipJoint =
            rightHand.GetJoint(XRHandJointID.IndexTip);

        XRHandJoint baseJoint =
            rightHand.GetJoint(XRHandJointID.IndexProximal);

        if (!tipJoint.TryGetPose(out Pose tipPose) ||
            !baseJoint.TryGetPose(out Pose basePose))
        {
            StopPointing();
            return;
        }

        Vector3 tip = ToWorldPosition(tipPose.position);
        Vector3 basePosition = ToWorldPosition(basePose.position);

        Vector3 direction = tip - basePosition;

        if (direction.sqrMagnitude < 0.0001f)
        {
            StopPointing();
            return;
        }

        direction.Normalize();

        Ray ray = new Ray(tip, direction);
        instrumentSelector.UpdatePointing(ray);

        if (showDebugRay)
        {
            Debug.DrawRay(
                tip,
                direction * rayDebugLength,
                Color.cyan
            );
        }
    }

    // ==========================================
    // PALM UP GAUCHE - VOLUME GLOBAL
    // ==========================================

    private void UpdatePalmUp()
    {
        if (!palmUpActive ||
            leftFistActive ||
            interactionManager == null ||
            headCamera == null)
            return;

        XRHand leftHand = hands.leftHand;

        if (!leftHand.isTracked)
            return;

        XRHandJoint palm =
            leftHand.GetJoint(XRHandJointID.Palm);

        if (!palm.TryGetPose(out Pose pose))
            return;

        interactionManager.SetGlobalVolume(
            GetNormalizedHandHeight(pose.position)
        );
    }

    // ==========================================
    // PALM UP DROIT - VOLUME LOCAL
    // ==========================================

    private void UpdateRightPalmUp()
    {
        if (!rightPalmUpActive ||
            rightFistActive ||
            thumbDownActive ||
            interactionManager == null ||
            headCamera == null)
            return;

        XRHand rightHand = hands.rightHand;

        if (!rightHand.isTracked)
            return;

        XRHandJoint palm =
            rightHand.GetJoint(XRHandJointID.Palm);

        if (!palm.TryGetPose(out Pose pose))
            return;

        interactionManager.SetSelectedGroupsVolume(
            GetNormalizedHandHeight(pose.position)
        );
    }

    // ==========================================
    // NORMALISATION
    // ==========================================

    private float GetNormalizedHandHeight(Vector3 localPosition)
    {
        Vector3 worldPosition = ToWorldPosition(localPosition);

        float relativeHeight =
            worldPosition.y - headCamera.transform.position.y;

        return Mathf.InverseLerp(
            minHeightBelowHead,
            maxHeightBelowHead,
            relativeHeight
        );
    }

    private Vector3 ToWorldPosition(Vector3 localPosition)
    {
        if (xrOrigin == null || xrOrigin.Origin == null)
            return localPosition;

        return xrOrigin.Origin.transform.TransformPoint(
            localPosition
        );
    }

    // ==========================================
    // RESET
    // ==========================================

    public void StopPointing()
    {
        pointInactiveTime = 0f;

        if (instrumentSelector != null)
            instrumentSelector.StopPointing();
    }

    private void OnDisable()
    {
        fingerPointActive = false;
        palmUpActive = false;
        rightPalmUpActive = false;

        leftFistActive = false;
        rightFistActive = false;
        thumbDownActive = false;

        StopPointing();
    }
}
