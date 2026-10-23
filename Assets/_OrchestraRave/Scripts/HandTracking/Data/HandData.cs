using UnityEngine;
using UnityEngine.XR.Hands;

[System.Serializable]
public struct HandData
{
    public bool IsTracked;
    public bool WristValid;
    public bool ThumbTipValid;
    public bool IndexTipValid;
    public bool MiddleTipValid;
    public bool RingTipValid;
    public bool LittleTipValid;
    public bool PalmValid;
    public bool IndexProximalValid;
    public bool IndexIntermediateValid;

    public Vector3 WristPosition;
    public Quaternion WristRotation;

    public Vector3 ThumbTipPosition;
    public Vector3 IndexTipPosition;
    public Vector3 MiddleTipPosition;
    public Vector3 RingTipPosition;
    public Vector3 LittleTipPosition;
    public float NormalizedHeight;
    public float WristRotationAngle;

    public bool IsOpen;
    public bool IsPointing;
    public bool IsPinching;

    public bool PinchStarted;
    public bool IsFist;
    public Vector3 PalmPosition;
    public Vector3 IndexProximalPosition;
    public Vector3 IndexIntermediatePosition;

    public Vector3 PointingRayOrigin;
    public Vector3 PointingRayDirection;

}

