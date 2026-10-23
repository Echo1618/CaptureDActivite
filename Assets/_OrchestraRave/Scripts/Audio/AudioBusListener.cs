
using UnityEngine;

public class AudioBusListener : MonoBehaviour
{
    [SerializeField]
    private AudioManager audioManager;

    private void Awake()
    {
        if (audioManager == null)
            audioManager = FindFirstObjectByType<AudioManager>();

        if (audioManager == null)
        {
            Debug.LogError(
                "[AudioBusListener] AudioManager introuvable.",
                this);
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
            return;

        switch (cmd.Type)
        {
            case OrchestraCommandType.SetGlobalVolume:
                audioManager.SetGlobalVolume(cmd.Value);
                break;

            case OrchestraCommandType.SetGlobalFilter:
                audioManager.SetGlobalFilter(cmd.Value);
                break;

            case OrchestraCommandType.SetGlobalMute:
                audioManager.SetGlobalMute(cmd.IsMuted);
                break;

            case OrchestraCommandType.SetGroupVolume:
                audioManager.SetFamilyVolume(
                    cmd.Target, cmd.Value);
                break;

            case OrchestraCommandType.SetGroupMute:
                audioManager.SetFamilyMute(
                    cmd.Target, cmd.IsMuted);
                break;

            case OrchestraCommandType.ChangePhase:
                audioManager.ChangePhase(cmd.Phase);
                break;

            default:
                Debug.LogWarning(
                    $"Commande audio inconnue : {cmd.Type}");
                break;
        }
    }
}
