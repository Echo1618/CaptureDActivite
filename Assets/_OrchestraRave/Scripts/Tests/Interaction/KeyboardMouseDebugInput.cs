
using UnityEngine;
using UnityEngine.InputSystem;

public class KeyboardMouseDebugInput : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera testCamera;
    [SerializeField] private InstrumentSelector instrumentSelector;
    [SerializeField] private OrchestraInteractionManager interactionManager;
    [SerializeField] private InteractionDebugPanel debugPanel;

    [Header("Settings")]
    [SerializeField, Range(0.05f, 2f)]
    private float parameterSpeed = 0.5f;
    private float simulatedGlobalVolume;
    private float simulatedGlobalFilter;
    private float simulatedGroupVolume = 1f;

    private void Awake()
    {
        if (testCamera == null)
            testCamera = Camera.main;
        if (interactionManager != null)
        {
            simulatedGlobalVolume = interactionManager.GlobalVolume;
            simulatedGlobalFilter = interactionManager.GlobalFilter;
        }
    }

    private void Update()
    {
        HandleMouse();
        HandleKeyboard();
    }

    private void HandleMouse()
    {
        if (Mouse.current == null ||
            testCamera == null ||
            instrumentSelector == null)
        {
            return;
        }

        Vector2 mousePosition =
            Mouse.current.position.ReadValue();

        Ray ray = testCamera.ScreenPointToRay(mousePosition);

        instrumentSelector.UpdatePointing(ray);
    }

    private void HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || interactionManager == null)
            return;

        float delta = parameterSpeed * Time.deltaTime;

        // Selection
        if (keyboard.cKey.wasPressedThisFrame)
            interactionManager.ClearGroupSelection();

        // Mute
        if (keyboard.mKey.wasPressedThisFrame)
            interactionManager.ToggleSelectedGroupsMute();

        // Global volume
        float vertical = 0f;
        float horizontal = 0f;

        if (keyboard.upArrowKey.isPressed) vertical += 1f;
        if (keyboard.downArrowKey.isPressed) vertical -= 1f;

        if (keyboard.rightArrowKey.isPressed) horizontal += 1f;
        if (keyboard.leftArrowKey.isPressed) horizontal -= 1f;

        if (vertical != 0f)
        {
            simulatedGlobalVolume = Mathf.Clamp01(
                simulatedGlobalVolume + vertical * delta);

            interactionManager.SetGlobalVolume(simulatedGlobalVolume);
        }

        if (horizontal != 0f)
        {
            simulatedGlobalFilter = Mathf.Clamp01(
                simulatedGlobalFilter + horizontal * delta);

            interactionManager.SetGlobalFilter(simulatedGlobalFilter);
        }

        // Group volume
        if (keyboard.pageUpKey.isPressed)
        {
            ChangeSelectedGroupsVolume(delta);
        }

        if (keyboard.pageDownKey.isPressed)
        {
            ChangeSelectedGroupsVolume(-delta);
        }

        // Music phases
        if (keyboard.digit1Key.wasPressedThisFrame)
            interactionManager.ChangePhase(MusicPhase.Classical);

        if (keyboard.digit2Key.wasPressedThisFrame)
            interactionManager.ChangePhase(MusicPhase.Transition);

        if (keyboard.digit3Key.wasPressedThisFrame)
            interactionManager.ChangePhase(MusicPhase.Techno);

        // Debug panel
        if (keyboard.f2Key.wasPressedThisFrame &&
            debugPanel != null)
        {
            debugPanel.ToggleVisible();
        }
    }

    private void ChangeSelectedGroupsVolume(float delta)
    {
        if (interactionManager.SelectedGroups.Count == 0)
            return;

        simulatedGroupVolume = Mathf.Clamp01(
            simulatedGroupVolume + delta);

        interactionManager.SetSelectedGroupsVolume(
            simulatedGroupVolume);
    }

    private void OnDisable()
    {
        if (instrumentSelector != null)
            instrumentSelector.StopPointing();
    }
}
