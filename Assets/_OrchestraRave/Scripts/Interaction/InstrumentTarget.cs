using UnityEngine;

public class InstrumentTarget : MonoBehaviour
{
    [SerializeField]
    private InstrumentGroupId groupId;

    public InstrumentGroupId GroupId => groupId;
}