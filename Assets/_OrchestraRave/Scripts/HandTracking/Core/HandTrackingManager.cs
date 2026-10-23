
using UnityEngine;
using UnityEngine.XR.Hands;

public class HandTrackingManager : MonoBehaviour
{
    public HandData LeftHand { get; private set; }
    public HandData RightHand { get; private set; }

    public bool IsLeftHandTracked => LeftHand.IsTracked;
    public bool IsRightHandTracked => RightHand.IsTracked;

    [Header("Height Calibration")]
    [SerializeField] private float minHandHeight = 0.5f;
    [SerializeField] private float maxHandHeight = 2.0f;

    [Header("Open Hand Detection")]
    [SerializeField, Range(0.01f, 0.15f)]
    private float openFingerDistance = 0.08f;

    private XRHandSubsystem handSubsystem;

    [Header("Pointing Detection")]
    [SerializeField, Range(0.05f, 0.15f)]
    private float indexExtendedDistance = 0.10f;

    [SerializeField, Range(0.03f, 0.10f)]
    private float foldedFingerDistance = 0.07f;

    [Header("Right Hand Pinch")]
    [SerializeField, Range(0.01f, 0.08f)]
    private float pinchStartDistance = 0.025f;

    [SerializeField, Range(0.01f, 0.10f)]
    private float pinchReleaseDistance = 0.04f;


    private void OnEnable()
    {
        FindHandSubsystem();
    }

    private void Update()
    {
        if (handSubsystem == null || !handSubsystem.running)
        {
            FindHandSubsystem();

            if (handSubsystem == null || !handSubsystem.running)
            {
                ResetHandData();
                return;
            }
        }

        // Conserver l'état précédent avant de mettre à jour les mains.
        bool wasRightPinching = RightHand.IsPinching;

        HandData leftData = LeftHand;
        HandData rightData = RightHand;

        UpdateHand(handSubsystem.leftHand, ref leftData);
        UpdateHand(handSubsystem.rightHand, ref rightData);

        // Détecter le pincement de la main droite.
        rightData.IsPinching =
            IsHandPinching(rightData, wasRightPinching);

        // Vrai uniquement au début du pincement.
        rightData.PinchStarted =
            rightData.IsPinching && !wasRightPinching;

        // Détection de la main gauche déjà existante.
        leftData.IsOpen = IsHandOpen(leftData);

        LeftHand = leftData;
        RightHand = rightData;
    }

    private void FindHandSubsystem()
    {
        var subsystems = new System.Collections.Generic.List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);

        handSubsystem = null;

        foreach (var subsystem in subsystems)
        {
            if (subsystem.running)
            {
                handSubsystem = subsystem;
                break;
            }
        }
    }


    private void UpdateHand(XRHand hand, ref HandData data)
    {
        if (!hand.isTracked)
        {
            data = default;
            return;
        }

        data = default;
        data.IsTracked = true;

        // 1. Lire les articulations et conserver leur validité.
        data.WristValid = ReadJoint(
            hand, XRHandJointID.Wrist,
            out data.WristPosition, out data.WristRotation);

        data.ThumbTipValid = ReadJoint(
            hand, XRHandJointID.ThumbTip,
            out data.ThumbTipPosition, out _);

        data.IndexTipValid = ReadJoint(
            hand, XRHandJointID.IndexTip,
            out data.IndexTipPosition, out _);

        data.MiddleTipValid = ReadJoint(
            hand, XRHandJointID.MiddleTip,
            out data.MiddleTipPosition, out _);

        data.RingTipValid = ReadJoint(
            hand, XRHandJointID.RingTip,
            out data.RingTipPosition, out _);

        data.LittleTipValid = ReadJoint(
            hand, XRHandJointID.LittleTip,
            out data.LittleTipPosition, out _);

        data.PalmValid = ReadJoint(
            hand, XRHandJointID.Palm,
            out data.PalmPosition, out _);

        data.IndexProximalValid = ReadJoint(
            hand, XRHandJointID.IndexProximal,
            out data.IndexProximalPosition, out _);

        data.IndexIntermediateValid = ReadJoint(
            hand, XRHandJointID.IndexIntermediate,
            out data.IndexIntermediatePosition, out _);

        // 2. Calculer la hauteur normalisée uniquement si le poignet est valide.
        data.NormalizedHeight = data.WristValid
            ? NormalizeHeight(data.WristPosition.y)
            : 0f;

        // 3. Calculer le rayon de pointage.
        data.PointingRayOrigin = data.IndexProximalValid
            ? data.IndexProximalPosition
            : Vector3.zero;

        Vector3 indexDirection = data.IndexTipPosition - data.IndexProximalPosition;

        data.PointingRayDirection =
            data.IndexTipValid &&
            data.IndexProximalValid &&
            indexDirection.sqrMagnitude > 0.0001f
                ? indexDirection.normalized
                : Vector3.zero;

        // 4. Vérifier si les doigts autres que l'index sont repliés.
        bool middleFolded =
            data.MiddleTipValid &&
            data.PalmValid &&
            Vector3.Distance(data.MiddleTipPosition, data.PalmPosition) < foldedFingerDistance;

        bool ringFolded = data.RingTipValid && data.PalmValid && Vector3.Distance(data.RingTipPosition, data.PalmPosition) < foldedFingerDistance;

        bool littleFolded =
            data.LittleTipValid &&
            data.PalmValid &&
            Vector3.Distance(
                data.LittleTipPosition,
                data.PalmPosition) < foldedFingerDistance;

        // 5. Vérifier si l'index est tendu.
        bool indexExtended =
            data.IndexTipValid &&
            data.PalmValid &&
            Vector3.Distance(
                data.IndexTipPosition,
                data.PalmPosition) > indexExtendedDistance;

        // 6. Reconnaître le geste de pointage.
        data.IsPointing =
            data.IsTracked &&
            data.PalmValid &&
            data.IndexProximalValid &&
            data.IndexIntermediateValid &&
            data.IndexTipValid &&
            data.MiddleTipValid &&
            data.RingTipValid &&
            data.LittleTipValid &&
            indexExtended &&
            middleFolded &&
            ringFolded &&
            littleFolded &&
            data.PointingRayDirection != Vector3.zero;
    }

    private bool ReadJoint(XRHand hand, XRHandJointID jointID, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var joint = hand.GetJoint(jointID);

        if (!joint.TryGetPose(out Pose pose))
            return false;

        position = pose.position;
        rotation = pose.rotation;
        return true;
    }

    private float NormalizeHeight(float height)
    {
        if (maxHandHeight <= minHandHeight)
            return 0f;

        return Mathf.Clamp01(
            (height - minHandHeight) /
            (maxHandHeight - minHandHeight)
        );
    }

    private bool IsHandOpen(HandData hand)
    {
        if (!hand.IsTracked || !hand.WristValid || !hand.ThumbTipValid || !hand.IndexTipValid || !hand.MiddleTipValid || !hand.RingTipValid || !hand.LittleTipValid)
        {
            return false;
        }

        Vector3 wrist = hand.WristPosition;

        bool thumbExtended = Vector3.Distance(wrist, hand.ThumbTipPosition) > openFingerDistance;

        bool indexExtended = Vector3.Distance(wrist, hand.IndexTipPosition) > openFingerDistance;

        bool middleExtended = Vector3.Distance(wrist, hand.MiddleTipPosition) > openFingerDistance;

        bool ringExtended = Vector3.Distance(wrist, hand.RingTipPosition) > openFingerDistance;

        bool littleExtended = Vector3.Distance(wrist, hand.LittleTipPosition) > openFingerDistance;

        return thumbExtended && indexExtended && middleExtended && ringExtended && littleExtended;
    }

    private bool IsHandPinching(HandData hand, bool wasPinching)
    {
        if (!hand.IsTracked || !hand.ThumbTipValid || !hand.IndexTipValid)
            return false;

        float threshold = wasPinching ? pinchReleaseDistance : pinchStartDistance;

        float distance = Vector3.Distance(
            hand.ThumbTipPosition,
            hand.IndexTipPosition
        );

        return distance <= threshold;
    }

    private void ResetHandData()
    {
        LeftHand = default;
        RightHand = default;
    }
}
