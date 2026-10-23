
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Hands;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Maestro.Core
{
    [DisallowMultipleComponent]
    public class MaestroDebugPanel : MonoBehaviour
    {
        [Header("UI")]
        public GameObject panel;
        public GameObject progressRoot;
        public Image progressImage;

        [Header("Geste mains jointes")]
        [Min(0.1f)]
        public float holdSeconds = 5f;

        [Min(0.01f)]
        public float maxPalmDistance = 0.10f;

        public float minHeight = 0.9f;
        public float maxHeight = 1.6f;

        [Range(-1f, 1f)]
        public float maxFacingDot = -0.6f;

        public float progressLift = 0.10f;

        [Header("Placement du panneau")]
        public float panelDistance = 0.8f;
        public float panelHeightOffset = -0.2f;

        [Header("Noms des racines de scène")]
        public string environmentName = "Environment";
        public string speakersName = "Speakers";

        public string[] sectionNames =
        {
            "Strings",
            "Vent",
            "Brass",
            "Percution"
        };

        public string stageLightsName = "StageLights";
        public string lasersName = "Lasers";

        private const string Tag = "[MaestroDebugPanel] ";

        private static readonly List<XRHandSubsystem> s_HandSubsystems =
            new List<XRHandSubsystem>();

        private Animator _environment;
        private Animator _speakers;

        private readonly List<Animator> _musicians =
            new List<Animator>();

        private readonly List<Light> _spots =
            new List<Light>();

        private readonly List<GameObject> _lasers =
            new List<GameObject>();

        private XRHandSubsystem _hands;
        private XROrigin _origin;

        private float _nextSubsystemSearch;
        private float _hold;
        private bool _latched;

        public bool IsPanelVisible =>
            panel != null && panel.activeSelf;

        public float HoldProgress =>
            holdSeconds > 0f
                ? Mathf.Clamp01(_hold / holdSeconds)
                : 0f;

        public bool HandTrackingAvailable =>
            _hands != null && _hands.running;

        // --------------------------------------------------
        // INITIALISATION
        // --------------------------------------------------

        private void Start()
        {
            FindTargets();

            if (panel != null)
                panel.SetActive(false);

            if (progressRoot != null)
                progressRoot.SetActive(false);
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (F1Pressed())
                TogglePanel();
#endif

            UpdateGesture();
        }

#if UNITY_EDITOR
        private static bool F1Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   Keyboard.current.f1Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F1);
#else
            return false;
#endif
        }
#endif

        // --------------------------------------------------
        // RECHERCHE DES OBJETS
        // --------------------------------------------------

        public void FindTargets()
        {
            _environment = null;
            _speakers = null;

            _musicians.Clear();
            _spots.Clear();
            _lasers.Clear();

            GameObject[] roots =
                SceneManager.GetActiveScene().GetRootGameObjects();

            GameObject env = FindRoot(roots, environmentName);

            if (env != null)
                _environment = env.GetComponent<Animator>();

            if (_environment == null)
            {
                Debug.LogWarning(
                    Tag + "Animator Environment introuvable."
                );
            }

            GameObject spk = FindRoot(roots, speakersName);

            if (spk != null)
                _speakers = spk.GetComponent<Animator>();

            if (_speakers == null)
            {
                Debug.LogWarning(
                    Tag + "Animator Speakers introuvable."
                );
            }

            foreach (string sectionName in sectionNames)
            {
                GameObject section = FindRoot(roots, sectionName);

                if (section == null)
                {
                    Debug.LogWarning(
                        Tag + "Section introuvable : " + sectionName
                    );

                    continue;
                }

                _musicians.AddRange(
                    section.GetComponentsInChildren<Animator>(true)
                );
            }

            GameObject lights = FindRoot(
                roots, stageLightsName
            );

            if (lights != null)
            {
                _spots.AddRange(
                    lights.GetComponentsInChildren<Light>(true)
                );
            }

            GameObject laserRoot = FindRoot(
                roots, lasersName
            );

            if (laserRoot != null)
            {
                foreach (Transform child in laserRoot.transform)
                {
                    _lasers.Add(child.gameObject);
                }
            }
        }

        private static GameObject FindRoot(
            GameObject[] roots,
            string objectName)
        {
            foreach (GameObject root in roots)
            {
                if (root.name == objectName)
                    return root;
            }

            return null;
        }

        // --------------------------------------------------
        // COMMANDES DES BOUTONS
        // --------------------------------------------------

        public void OnRave()
        {
            Trigger(_environment, "Rave", environmentName);
        }

        public void OnReset()
        {
            Trigger(_environment, "Reset", environmentName);
        }

        public void OnDrop()
        {
            Trigger(_speakers, "Drop", speakersName);
        }

        public void OnMusiciansPlay()
        {
            MusiciansTrigger("Play");
        }

        public void OnMusiciansStop()
        {
            MusiciansTrigger("Stop");
        }

        public void OnMusiciansHeadbang()
        {
            MusiciansTrigger("Headbang");
        }

        public void OnToggleSpots()
        {
            if (_spots.Count == 0)
            {
                Debug.LogWarning(Tag + "Aucun spot trouvé.");
                return;
            }

            bool anyOn = false;

            foreach (Light light in _spots)
            {
                if (light != null && light.enabled)
                {
                    anyOn = true;
                    break;
                }
            }

            foreach (Light light in _spots)
            {
                if (light != null)
                    light.enabled = !anyOn;
            }
        }

        public void OnToggleLasers()
        {
            if (_lasers.Count == 0)
            {
                Debug.LogWarning(Tag + "Aucun laser trouvé.");
                return;
            }

            bool anyOn = false;

            foreach (GameObject laser in _lasers)
            {
                if (laser != null && laser.activeSelf)
                {
                    anyOn = true;
                    break;
                }
            }

            foreach (GameObject laser in _lasers)
            {
                if (laser != null)
                    laser.SetActive(!anyOn);
            }
        }

        public void OnClose()
        {
            HidePanel();
        }

        private void Trigger(
            Animator animator,
            string triggerName,
            string targetName)
        {
            if (animator == null)
            {
                Debug.LogWarning(
                    Tag + targetName + " introuvable : " +
                    triggerName + " ignoré."
                );

                return;
            }

            animator.SetTrigger(triggerName);
        }

        private void MusiciansTrigger(string triggerName)
        {
            foreach (Animator animator in _musicians)
            {
                if (animator != null)
                    animator.SetTrigger(triggerName);
            }
        }

        // --------------------------------------------------
        // AFFICHAGE DU PANNEAU
        // --------------------------------------------------

        public void TogglePanel()
        {
            if (panel == null)
                return;

            if (panel.activeSelf)
                HidePanel();
            else
                ShowPanel();
        }

        public void ShowPanel()
        {
            if (panel == null)
                return;

            Camera cam = HeadCamera();

            if (cam != null)
            {
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;

                forward.Normalize();

                Vector3 position =
                    cam.transform.position +
                    forward * panelDistance +
                    Vector3.up * panelHeightOffset;

                panel.transform.SetPositionAndRotation(
                    position,
                    Quaternion.LookRotation(forward, Vector3.up)
                );

                Canvas canvas = panel.GetComponent<Canvas>();

                if (canvas != null && canvas.worldCamera == null)
                    canvas.worldCamera = cam;
            }

            panel.SetActive(true);
        }

        public void HidePanel()
        {
            if (panel != null)
                panel.SetActive(false);
        }

        private Camera HeadCamera()
        {
            Camera cam = Camera.main;

            if (cam != null)
                return cam;

            if (_origin == null)
                _origin = FindFirstObjectByType<XROrigin>();

            return _origin != null ? _origin.Camera : null;
        }

        // --------------------------------------------------
        // GESTE MAINS JOINTES
        // --------------------------------------------------

        private void UpdateGesture()
        {
            if (_hands == null || !_hands.running)
            {
                if (Time.unscaledTime >= _nextSubsystemSearch)
                {
                    _nextSubsystemSearch = Time.unscaledTime + 1f;

                    _hands = null;
                    s_HandSubsystems.Clear();

                    SubsystemManager.GetSubsystems(
                        s_HandSubsystems
                    );

                    foreach (XRHandSubsystem subsystem
                             in s_HandSubsystems)
                    {
                        if (subsystem.running)
                        {
                            _hands = subsystem;
                            break;
                        }
                    }
                }

                if (_hands == null || !_hands.running)
                {
                    Hold(false, Vector3.zero);
                    return;
                }
            }

            XRHand left = _hands.leftHand;
            XRHand right = _hands.rightHand;

            bool conditionMet = false;
            Vector3 midpoint = Vector3.zero;

            if (left.isTracked &&
                right.isTracked &&
                left.GetJoint(XRHandJointID.Palm)
                    .TryGetPose(out Pose leftPose) &&
                right.GetJoint(XRHandJointID.Palm)
                    .TryGetPose(out Pose rightPose))
            {
                if (_origin == null)
                    _origin = FindFirstObjectByType<XROrigin>();

                Transform originTransform =
                    _origin != null
                        ? _origin.Origin.transform
                        : null;

                Vector3 leftPos =
                    originTransform != null
                        ? originTransform.TransformPoint(
                            leftPose.position)
                        : leftPose.position;

                Vector3 rightPos =
                    originTransform != null
                        ? originTransform.TransformPoint(
                            rightPose.position)
                        : rightPose.position;

                Vector3 leftBack =
                    leftPose.rotation * Vector3.up;

                Vector3 rightBack =
                    rightPose.rotation * Vector3.up;

                if (originTransform != null)
                {
                    leftBack = originTransform.TransformDirection(
                        leftBack
                    );

                    rightBack = originTransform.TransformDirection(
                        rightBack
                    );
                }

                midpoint = (leftPos + rightPos) * 0.5f;

                float floorY =
                    originTransform != null
                        ? originTransform.position.y
                        : 0f;

                float height = midpoint.y - floorY;

                conditionMet =
                    Vector3.Distance(leftPos, rightPos)
                        <= maxPalmDistance &&
                    height >= minHeight &&
                    height <= maxHeight &&
                    Vector3.Dot(
                        leftBack.normalized,
                        rightBack.normalized)
                        <= maxFacingDot;
            }

            Hold(conditionMet, midpoint);
        }

        private void Hold(
            bool conditionMet,
            Vector3 midpoint)
        {
            if (!conditionMet)
            {
                _hold = 0f;
                _latched = false;

                ShowProgress(false, midpoint);
                return;
            }

            if (_latched)
            {
                ShowProgress(false, midpoint);
                return;
            }

            _hold += Time.unscaledDeltaTime;

            ShowProgress(true, midpoint);

            if (_hold >= holdSeconds)
            {
                _hold = 0f;
                _latched = true;

                ShowProgress(false, midpoint);
                TogglePanel();
            }
        }

        private void ShowProgress(
            bool visible,
            Vector3 midpoint)
        {
            if (progressRoot == null)
                return;

            if (progressRoot.activeSelf != visible)
                progressRoot.SetActive(visible);

            if (!visible)
                return;

            Vector3 position =
                midpoint + Vector3.up * progressLift;

            Camera cam = HeadCamera();

            Quaternion rotation =
                cam != null
                    ? Quaternion.LookRotation(
                        position - cam.transform.position,
                        Vector3.up)
                    : Quaternion.identity;

            progressRoot.transform.SetPositionAndRotation(
                position,
                rotation
            );

            if (progressImage != null)
                progressImage.fillAmount = HoldProgress;
        }

        private void OnDisable()
        {
            _hold = 0f;
            _latched = false;

            if (progressRoot != null)
                progressRoot.SetActive(false);
        }
    }
}
