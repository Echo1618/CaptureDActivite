
using System;
using UnityEngine;

public class InstrumentTarget : MonoBehaviour
{
    [SerializeField]
    private InstrumentGroupId groupId = InstrumentGroupId.None;

    public InstrumentGroupId GroupId => groupId;

    private void OnValidate()
    {
        if (groupId == InstrumentGroupId.None ||
            !Enum.IsDefined(typeof(InstrumentGroupId), groupId))
        {
            Debug.LogWarning(
                $"InstrumentTarget sur {gameObject.name} : " +
                "aucun groupe instrumental valide assigné.",
                this
            );
        }
    }
}
