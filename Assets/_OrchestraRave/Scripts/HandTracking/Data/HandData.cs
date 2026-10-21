using UnityEngine;
using UnityEngine.XR.Hands;

[System.Serializable]
public struct HandData
{
    public bool IsTracked;

    public Vector3 WristPosition;
    public Quaternion WristRotation;

    public Vector3 ThumbTipPosition;
    public Vector3 IndexTipPosition;
    public Vector3 MiddleTipPosition;
    public Vector3 RingTipPosition;
    public Vector3 LittleTipPosition;
}

