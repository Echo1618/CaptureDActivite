
using UnityEngine;
using UnityEngine.XR.Hands;

public class HandTrackingManager : MonoBehaviour
{
    public HandData LeftHand { get; private set; }
    public HandData RightHand { get; private set; }

    public bool IsLeftHandTracked => LeftHand.IsTracked;
    public bool IsRightHandTracked => RightHand.IsTracked;

    private XRHandSubsystem handSubsystem;

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

        HandData leftData = LeftHand;
        HandData rightData = RightHand;

        UpdateHand(handSubsystem.leftHand, ref leftData);
        UpdateHand(handSubsystem.rightHand, ref rightData);

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

        data.WristPosition = Vector3.zero;
        data.WristRotation = Quaternion.identity;

        ReadJoint(hand, XRHandJointID.Wrist,
            out data.WristPosition, out data.WristRotation);

        ReadJoint(hand, XRHandJointID.ThumbTip,
            out data.ThumbTipPosition, out _);

        ReadJoint(hand, XRHandJointID.IndexTip,
            out data.IndexTipPosition, out _);

        ReadJoint(hand, XRHandJointID.MiddleTip,
            out data.MiddleTipPosition, out _);

        ReadJoint(hand, XRHandJointID.RingTip,
            out data.RingTipPosition, out _);

        ReadJoint(hand, XRHandJointID.LittleTip,
            out data.LittleTipPosition, out _);
    }


    private bool ReadJoint(
        XRHand hand,
        XRHandJointID jointID,
        out Vector3 position,
        out Quaternion rotation)
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

    private void ResetHandData()
    {
        LeftHand = default;
        RightHand = default;
    }
}
