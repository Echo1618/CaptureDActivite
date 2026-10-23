using Maestro.Environment;
using UnityEngine;

// Exemple à recopier dans votre script de hand tracking : un geste détecté lance la séquence rave. Ne pas le poser dans la scène.
public class RaveGestureBridge : MonoBehaviour
{
    public bool gestureDetected; // à mettre à vrai depuis votre détection de geste

    void Start()
    {
        // Liste dans la Console les cibles de la DA trouvées ou manquantes dans la scène.
        Debug.Log(MaestroOrchestraApi.Diagnose());
        // Journal des phases : Countdown, Drop, Transform, Party, puis Idle à l'arrêt (se désabonner si l'objet est détruit).
        MaestroOrchestraApi.OnRavePhaseChanged += phase => Debug.Log("[RaveGestureBridge] phase : " + phase);
    }

    void Update()
    {
        // Un geste lance la séquence une seule fois ; pendant qu'elle tourne, le geste est ignoré.
        if (gestureDetected && !MaestroOrchestraApi.IsRaveSequenceRunning)
        {
            gestureDetected = false;
            MaestroOrchestraApi.StartRaveSequence();
        }
    }
}
