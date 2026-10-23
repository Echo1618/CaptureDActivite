
using UnityEngine;

public class MoveToGameObject : MonoBehaviour
{
    [SerializeField] private bool moveOnStart = true;
    [SerializeField] private Transform target;

    private void Start()
    {
        if (!moveOnStart)
            return;

        if (target == null)
        {
            Debug.LogWarning("Aucune destination définie.", this);
            return;
        }

        transform.position = target.position;
    }
}
