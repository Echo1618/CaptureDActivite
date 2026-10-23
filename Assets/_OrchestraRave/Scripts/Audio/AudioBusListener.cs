
using UnityEngine;

public class AudioBusListener : MonoBehaviour
{
    [Header("Référence AudioManager")]
    [SerializeField] private AudioManager audioManager;

    private void Awake()
    {
        if (audioManager == null)
        {
            audioManager = FindFirstObjectByType<AudioManager>();
        }

        if (audioManager == null)
        {
            Debug.LogError(
                "[AudioBusListener] Aucun AudioManager trouvé dans la scène.",
                this
            );
        }
    }

    private void OnEnable()
    {
        OrchestraEventBus.OnCommand += HandleCommand;
    }

    private void OnDisable()
    {
        OrchestraEventBus.OnCommand -= HandleCommand;
    }

    private void HandleCommand(OrchestraCommand cmd)
    {
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[AudioBusListener] AudioManager introuvable."
            );
            return;
        }

        switch (cmd.Type)
        {
            case OrchestraCommandType.SetGlobalVolume:
                // Volume global entre 0 et 1
                audioManager.SetGlobalVolume(cmd.Value);
                break;

            case OrchestraCommandType.SetGlobalFilter:
                // Le filtre global n'est pas encore implémenté
                Debug.LogWarning(
                    "[AudioBusListener] SetGlobalFilter non implémenté."
                );
                break;

            case OrchestraCommandType.SetGroupVolume:
                // Volume d'une famille entre 0 et 1
                audioManager.SetFamilyVolume(
                    cmd.Target,
                    cmd.Value
                );
                break;

            case OrchestraCommandType.SetGroupMute:
                // Mute ou unmute d'une famille
                audioManager.SetFamilyMute(
                    cmd.Target,
                    cmd.IsMuted
                );
                break;

            case OrchestraCommandType.ChangePhase:
                // Classical, Transition ou Techno
                audioManager.ChangePhase(cmd.Phase);
                break;

            default:
                Debug.LogWarning(
                    $"[AudioBusListener] Commande inconnue : {cmd.Type}"
                );
                break;
        }
    }
}
