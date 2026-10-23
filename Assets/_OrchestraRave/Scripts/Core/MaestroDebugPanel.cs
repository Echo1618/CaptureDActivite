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
    /// <summary>
    /// Panneau de debug VR : outil de test, pas du gameplay. C'est le seul script du greybox qui lit le hand tracking.
    /// Geste « mains jointes » (paumes face à face à moins de 10 cm, à hauteur de poitrine, tenues 5 s) via XRHandSubsystem
    /// pour afficher / cacher le panneau ; F1 dans l'éditeur. Les boutons sont câblés (listeners persistants) par le build
    /// sur les méthodes On* ci-dessous, qui agissent sur les instances de la scène trouvées au démarrage par nom de racine.
    /// </summary>
    [DisallowMultipleComponent]
    public class MaestroDebugPanel : MonoBehaviour
    {
        [Header("UI (remplie par le build)")]
        public GameObject panel;
        public GameObject progressRoot;
        public Image progressImage;

        [Header("Geste mains jointes")]
        public float holdSeconds = 5f;
        public float maxPalmDistance = 0.10f;
        public float minHeight = 0.9f;
        public float maxHeight = 1.6f;
        [Tooltip("Produit scalaire maximal entre les « dos de main » des deux paumes (face à face = -1).")]
        public float maxFacingDot = -0.6f;
        [Tooltip("Le cercle de progression est levé de cette hauteur au-dessus du point médian des paumes pour rester visible.")]
        public float progressLift = 0.10f;

        [Header("Placement du panneau")]
        public float panelDistance = 0.8f;
        public float panelHeightOffset = -0.2f;

        [Header("Noms des racines de scène")]
        public string environmentName = "Environment";
        public string speakersName = "Speakers";
        public string[] sectionNames = { "Strings", "Vent", "Brass", "Percution" };
        public string stageLightsName = "StageLights";
        public string lasersName = "Lasers";
        [Tooltip("Groupe (sous la racine Environment) des poursuites de scène et du fill : le bouton Poursuites on/off bascule enabled sur ses Lights.")]
        public string poursuitesName = "Poursuites";

        const string Tag = "[MaestroDebugPanel] ";
        static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>();

        Animator _environment;
        Animator _speakers;
        readonly List<Animator> _musicians = new List<Animator>();
        readonly List<Light> _spots = new List<Light>();
        readonly List<GameObject> _lasers = new List<GameObject>();
        readonly List<Light> _poursuites = new List<Light>();

        XRHandSubsystem _hands;
        XROrigin _origin;
        float _nextSubsystemSearch;
        float _hold;
        bool _latched;

        public bool IsPanelVisible { get { return panel != null && panel.activeSelf; } }
        public float HoldProgress { get { return holdSeconds > 0f ? Mathf.Clamp01(_hold / holdSeconds) : 0f; } }
        public bool HandTrackingAvailable { get { return _hands != null && _hands.running; } }

        void Start()
        {
            FindTargets();
            if (panel != null) panel.SetActive(false);
            if (progressRoot != null) progressRoot.SetActive(false);
        }

        void Update()
        {
#if UNITY_EDITOR
            if (F1Pressed()) TogglePanel();
#endif
            UpdateGesture();
        }

#if UNITY_EDITOR
        static bool F1Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F1);
#else
            return false;
#endif
        }
#endif

        // ------------------------------------------------------------------ cibles

        /// <summary>Retrouve les instances de la scène par nom de racine ; message Console clair si une cible manque.</summary>
        public void FindTargets()
        {
            _environment = null;
            _speakers = null;
            _musicians.Clear();
            _spots.Clear();
            _lasers.Clear();
            _poursuites.Clear();
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

            GameObject env = FindRoot(roots, environmentName);
            if (env != null) _environment = env.GetComponent<Animator>();
            if (_environment == null) Debug.LogWarning(Tag + "cible manquante : racine « " + environmentName + " » avec un Animator (triggers Rave / Reset).");
            if (env != null)
            {
                foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
                    if (t.name == poursuitesName) { _poursuites.AddRange(t.GetComponentsInChildren<Light>(true)); break; }
            }
            if (_poursuites.Count == 0) Debug.LogWarning(Tag + "cible manquante : groupe « " + poursuitesName + " » avec des Lights sous « " + environmentName + " ».");

            GameObject spk = FindRoot(roots, speakersName);
            if (spk != null) _speakers = spk.GetComponent<Animator>();
            if (_speakers == null) Debug.LogWarning(Tag + "cible manquante : racine « " + speakersName + " » avec un Animator (trigger Drop).");

            foreach (string name in sectionNames)
            {
                GameObject s = FindRoot(roots, name);
                if (s == null) { Debug.LogWarning(Tag + "cible manquante : section « " + name + " »."); continue; }
                _musicians.AddRange(s.GetComponentsInChildren<Animator>(true));
            }
            if (_musicians.Count == 0) Debug.LogWarning(Tag + "aucun Animator de musicien trouvé sous les sections.");

            GameObject sl = FindRoot(roots, stageLightsName);
            if (sl != null) _spots.AddRange(sl.GetComponentsInChildren<Light>(true));
            if (_spots.Count == 0) Debug.LogWarning(Tag + "cible manquante : racine « " + stageLightsName + " » avec des Lights.");

            GameObject lz = FindRoot(roots, lasersName);
            if (lz != null) foreach (Transform c in lz.transform) _lasers.Add(c.gameObject);
            if (_lasers.Count == 0) Debug.LogWarning(Tag + "cible manquante : racine « " + lasersName + " » avec des enfants.");
        }

        static GameObject FindRoot(GameObject[] roots, string name)
        {
            foreach (GameObject r in roots) if (r.name == name) return r;
            return null;
        }

        // ------------------------------------------------------------------ boutons (câblés par le build)

        public void OnRave() { Trigger(_environment, "Rave", environmentName); }
        public void OnReset() { Trigger(_environment, "Reset", environmentName); }
        public void OnDrop() { Trigger(_speakers, "Drop", speakersName); }
        public void OnMusiciansPlay() { MusiciansTrigger("Play"); }
        public void OnMusiciansStop() { MusiciansTrigger("Stop"); }
        public void OnMusiciansHeadbang() { MusiciansTrigger("Headbang"); }

        public void OnToggleSpots()
        {
            if (_spots.Count == 0) { Debug.LogWarning(Tag + "Spots : aucune Light sous « " + stageLightsName + " »."); return; }
            bool anyOn = false;
            foreach (Light l in _spots) if (l != null && l.enabled) { anyOn = true; break; }
            foreach (Light l in _spots) if (l != null) l.enabled = !anyOn;
        }

        public void OnToggleLasers()
        {
            if (_lasers.Count == 0) { Debug.LogWarning(Tag + "Lasers : aucun enfant sous « " + lasersName + " »."); return; }
            bool anyOn = false;
            foreach (GameObject g in _lasers) if (g != null && g.activeSelf) { anyOn = true; break; }
            foreach (GameObject g in _lasers) if (g != null) g.SetActive(!anyOn);
        }

        /// <summary>v8 : bascule enabled sur les poursuites de scène et le fill (EnvironmentMood respecte l'état tant que l'ambiance ne change pas ; Reset les rallume).</summary>
        public void OnTogglePoursuites()
        {
            if (_poursuites.Count == 0) { Debug.LogWarning(Tag + "Poursuites : aucune Light sous « " + poursuitesName + " »."); return; }
            bool anyOn = false;
            foreach (Light l in _poursuites) if (l != null && l.enabled) { anyOn = true; break; }
            foreach (Light l in _poursuites) if (l != null) l.enabled = !anyOn;
        }

        public void OnClose() { HidePanel(); }

        void Trigger(Animator a, string trigger, string targetName)
        {
            if (a == null) { Debug.LogWarning(Tag + "« " + targetName + " » introuvable : trigger " + trigger + " ignoré."); return; }
            a.SetTrigger(trigger);
        }

        void MusiciansTrigger(string trigger)
        {
            if (_musicians.Count == 0) { Debug.LogWarning(Tag + "aucun musicien : trigger " + trigger + " ignoré."); return; }
            foreach (Animator a in _musicians) if (a != null) a.SetTrigger(trigger);
        }

        // ------------------------------------------------------------------ panneau

        public void TogglePanel()
        {
            if (panel == null) return;
            if (panel.activeSelf) HidePanel(); else ShowPanel();
        }

        /// <summary>Place le panneau à 0.8 m devant la tête, à hauteur des yeux - 0.2 m, face au joueur, puis le laisse fixe dans le monde.</summary>
        public void ShowPanel()
        {
            if (panel == null) return;
            Camera cam = HeadCamera();
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 pos = cam.transform.position + fwd * panelDistance + Vector3.up * panelHeightOffset;
                panel.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
                Canvas canvas = panel.GetComponent<Canvas>();
                if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = cam; // clics souris dans l'éditeur
            }
            panel.SetActive(true);
        }

        public void HidePanel()
        {
            if (panel != null) panel.SetActive(false);
        }

        Camera HeadCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) return cam;
            if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
            return _origin != null ? _origin.Camera : null;
        }

        // ------------------------------------------------------------------ geste mains jointes

        void UpdateGesture()
        {
            if (_hands == null || !_hands.running)
            {
                if (Time.unscaledTime >= _nextSubsystemSearch)
                {
                    _nextSubsystemSearch = Time.unscaledTime + 1f;
                    _hands = null;
                    SubsystemManager.GetSubsystems(s_HandSubsystems);
                    foreach (XRHandSubsystem s in s_HandSubsystems) if (s.running) { _hands = s; break; }
                }
                if (_hands == null) { Hold(false, Vector3.zero); return; }
            }

            XRHand left = _hands.leftHand;
            XRHand right = _hands.rightHand;
            bool ok = false;
            Vector3 mid = Vector3.zero;
            Pose pl, pr;
            if (left.isTracked && right.isTracked
                && left.GetJoint(XRHandJointID.Palm).TryGetPose(out pl)
                && right.GetJoint(XRHandJointID.Palm).TryGetPose(out pr))
            {
                // Les poses des articulations sont relatives au XR Origin : passage en monde.
                if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
                Transform o = _origin != null ? _origin.Origin.transform : null;
                Vector3 posL = o != null ? o.TransformPoint(pl.position) : pl.position;
                Vector3 posR = o != null ? o.TransformPoint(pr.position) : pr.position;
                Vector3 backL = pl.rotation * Vector3.up;
                Vector3 backR = pr.rotation * Vector3.up;
                if (o != null) { backL = o.TransformDirection(backL); backR = o.TransformDirection(backR); }
                float floorY = o != null ? o.position.y : 0f;
                mid = (posL + posR) * 0.5f;
                float height = mid.y - floorY;
                ok = Vector3.Distance(posL, posR) <= maxPalmDistance
                     && height >= minHeight && height <= maxHeight
                     && Vector3.Dot(backL, backR) <= maxFacingDot;
            }
            Hold(ok, mid);
        }

        void Hold(bool conditionMet, Vector3 mid)
        {
            if (!conditionMet)
            {
                _hold = 0f;
                _latched = false;
                ShowProgress(false, mid);
                return;
            }
            if (_latched) { ShowProgress(false, mid); return; } // déjà déclenché : attendre que les mains se séparent
            _hold += Time.deltaTime;
            ShowProgress(true, mid);
            if (_hold >= holdSeconds)
            {
                _hold = 0f;
                _latched = true;
                ShowProgress(false, mid);
                TogglePanel();
            }
        }

        void ShowProgress(bool visible, Vector3 mid)
        {
            if (progressRoot == null) return;
            if (progressRoot.activeSelf != visible) progressRoot.SetActive(visible);
            if (!visible) return;
            Vector3 pos = mid + Vector3.up * progressLift;
            Camera cam = HeadCamera();
            Quaternion rot = cam != null ? Quaternion.LookRotation(pos - cam.transform.position, Vector3.up) : Quaternion.identity;
            progressRoot.transform.SetPositionAndRotation(pos, rot);
            if (progressImage != null) progressImage.fillAmount = HoldProgress;
        }
    }
}
