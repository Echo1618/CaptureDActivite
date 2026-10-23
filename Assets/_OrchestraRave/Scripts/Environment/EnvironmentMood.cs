using UnityEngine;
using UnityEngine.Rendering;

namespace Maestro.Environment
{
    /// <summary>
    /// Ambiance de la scène : opéra (rave = 0) → château de nuit (rave = 1).
    /// Le champ <see cref="rave"/> est animé par l'Animator « EnvironmentController » de la racine Environment ;
    /// tout le reste (Directional Light, ambiante, brouillard, skybox, lune, lumières opéra / torches / braseros)
    /// est interpolé ici dans LateUpdate. Les déplacements des coques (OperaShell / CastleShell) sont faits par
    /// l'Animator, jamais par ce script. Références remplies par Tools/Maestro/Build Greybox.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnvironmentMood : MonoBehaviour
    {
        [Range(0f, 1f)] public float rave = 0f;

        [Header("Références (remplies par le build)")]
        public Light directionalLight;
        public Light[] operaLights;
        public float[] operaLightBases;
        public Light[] torchLights;
        public Light[] brazierLights;
        public Renderer moonRenderer;
        public Material nightSkybox;

        [Header("Bases et cibles")]
        public float torchBase = 2.5f;
        public float brazierBase = 3f;
        public float moonMaxScale = 40f;
        [Tooltip("Intensité émissive de la lune à rave = 1, en EV (facteur 2^n), comme le color picker HDR.")]
        public float moonEmissionIntensity = 2f;
        public Color moonEmissionColor = new Color(0.85f, 0.90f, 1.00f);
        public Color moonLightColor = new Color(180f / 255f, 200f / 255f, 255f / 255f);
        public float moonLightIntensity = 0.3f;
        public Color ambientOpera = new Color(0.40f, 0.32f, 0.26f);
        public Color ambientNight = new Color(0.07f, 0.09f, 0.18f);
        public Color fogNight = new Color(0.02f, 0.03f, 0.08f);
        public float fogDensityNight = 0.008f;
        public float skyboxExposure = 0.25f;

        [Header("Réflexions d'environnement (v9)")]
        [Tooltip("Cubemap uniforme créé par le build (CM_FlatReflection) : en mode édition les réflexions d'environnement sont plates (couleur ambiante opéra) au lieu de la skybox ; en Play Mode une instance est recolorée chaque frame avec l'ambiant interpolé.")]
        public Cubemap flatReflection;
        [Tooltip("Gain sur la couleur ambiante pour la couleur des réflexions.")]
        public float reflectionGain = 1f;

        [Header("Animator (v9)")]
        [Tooltip("Paramètre float du contrôleur recopié depuis rave chaque frame : les transitions Any State ne partent que des états au repos (Level ≤ 0.001 pour Rave, ≥ 0.999 pour Reset).")]
        public string levelParameter = "Level";
        public string raveTrigger = "Rave";
        public string resetTrigger = "Reset";
        public string raveState = "Rave";
        public string toOperaState = "ToOpera";
        public string operaState = "Opera";
        public const int FlatReflectionSize = 4;

        [Header("Party (v11 : ambiance néon, pilotée par RaveSequence)")]
        [Range(0f, 1f)] [Tooltip("1 en phase Party ; interpolé en partyFadeSeconds. Remis à 0 dès que l'Animator quitte l'état Rave (Reset).")]
        public float party = 0f;
        public float partyFadeSeconds = 1f;
        [Range(0f, 1f)] [Tooltip("Compte à rebours : les lumières d'opéra sont multipliées par (1 − operaDim).")]
        public float operaDim = 0f;
        public Color torchColor = new Color(1f, 150f / 255f, 60f / 255f);
        public Color torchColorParty = new Color(1f, 40f / 255f, 200f / 255f);
        public float torchPartyGain = 1.3f;
        public Color moonEmissionColorParty = new Color(120f / 255f, 230f / 255f, 1f);
        public float moonPartyGain = 1.5f;
        public Color ambientParty = new Color(0.10f, 0.02f, 0.16f);
        public Color fogParty = new Color(0.08f, 0f, 0.12f);
        public float fogDensityParty = 0.012f;
        public Color moonLightColorParty = new Color(160f / 255f, 120f / 255f, 1f);
        public float moonLightIntensityParty = 0.25f;
        [Tooltip("Bandes néon (CastleShell/Neons, remplies par le build) : désactivées hors party, allumées en cascade quand le niveau de party dépasse neonThreshold.")]
        public GameObject[] neons = new GameObject[0];
        public float neonThreshold = 0.5f;
        public float neonCascadeStep = 0.06f;

        /// <summary>Niveau de party effectif (interpolé).</summary>
        public float PartyLevel { get { return _partyLevel; } }
        public int NeonsActive { get { int n = 0; if (neons != null) foreach (GameObject g in neons) if (g != null && g.activeSelf) n++; return n; } }
        float _partyLevel;
        bool _neonsOn;
        float _neonStart;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        Material _skyInstance;
        MaterialPropertyBlock _moonBlock;
        bool[] _operaLightWasOn;
        Cubemap _reflInstance;
        Color[] _reflPixels;
        Color _reflColor = new Color(-1f, -1f, -1f, -1f);
        Animator _animator;
        bool _hasLevel, _hasTriggers;
        int _levelId, _raveId, _resetId;

        void Awake()
        {
            if (directionalLight == null) directionalLight = RenderSettings.sun;
            if (nightSkybox != null)
            {
                _skyInstance = new Material(nightSkybox);
                _skyInstance.name = nightSkybox.name + " (instance)";
                _skyInstance.SetFloat(ExposureId, skyboxExposure);
                RenderSettings.skybox = _skyInstance;
            }
            // v9 : réflexions d'environnement plates (cubemap 4 px recoloré avec l'ambiant) : le cubemap de la skybox procédurale, bleu-cyan,
            // teintait toutes les surfaces du théâtre dès que l'ambiant baissait pendant la transition (textures noyées).
            _reflInstance = new Cubemap(FlatReflectionSize, TextureFormat.RGBAHalf, false);
            _reflInstance.name = "CM_FlatReflection (instance)";
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = _reflInstance;
            _animator = GetComponent<Animator>();
            _hasLevel = HasParameter(levelParameter);
            _hasTriggers = HasParameter(raveTrigger) && HasParameter(resetTrigger);
            _levelId = Animator.StringToHash(levelParameter);
            _raveId = Animator.StringToHash(raveTrigger);
            _resetId = Animator.StringToHash(resetTrigger);
            if (neons != null) foreach (GameObject g in neons) if (g != null && g.activeSelf) g.SetActive(false);
            _moonBlock = new MaterialPropertyBlock();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fogMode = FogMode.Exponential;
            Apply(rave);
        }

        void LateUpdate()
        {
            _partyLevel = Mathf.MoveTowards(_partyLevel, Mathf.Clamp01(party), partyFadeSeconds > 0f ? Time.deltaTime / partyFadeSeconds : 1f);
            Apply(rave);
            UpdateNeons();
            GuardTransitions();
        }

        void OnDestroy()
        {
            if (_skyInstance != null) Destroy(_skyInstance);
            if (_reflInstance != null) Destroy(_reflInstance);
            if (flatReflection != null) RenderSettings.customReflectionTexture = flatReflection;
        }

        /// <summary>Applique l'interpolation opéra (0) → nuit (1). Appelé chaque frame avec <see cref="rave"/>.</summary>
        public void Apply(float t)
        {
            t = Mathf.Clamp01(t);

            if (directionalLight != null)
            {
                directionalLight.color = Color.Lerp(moonLightColor, moonLightColorParty, _partyLevel);
                directionalLight.intensity = Mathf.Lerp(0f, Mathf.Lerp(moonLightIntensity, moonLightIntensityParty, _partyLevel), t);
                directionalLight.enabled = directionalLight.intensity > 0.001f;
            }

            Color ambient = Color.Lerp(ambientOpera, Color.Lerp(ambientNight, ambientParty, _partyLevel), t);
            RenderSettings.ambientLight = ambient;
            float density = Mathf.Lerp(0f, Mathf.Lerp(fogDensityNight, fogDensityParty, _partyLevel), t);
            RenderSettings.fogColor = Color.Lerp(fogNight, fogParty, _partyLevel);
            RenderSettings.fogDensity = density;
            RenderSettings.fog = density > 0.00001f;
            ApplyReflection(ambient * reflectionGain);

            if (moonRenderer != null)
            {
                float u = Mathf.InverseLerp(0.5f, 1f, t); // échelle 0 tant que rave < 0.5
                moonRenderer.transform.localScale = Vector3.one * (moonMaxScale * u);
                if (_moonBlock == null) _moonBlock = new MaterialPropertyBlock();
                moonRenderer.GetPropertyBlock(_moonBlock);
                _moonBlock.SetColor(EmissionColorId, Color.Lerp(moonEmissionColor, moonEmissionColorParty, _partyLevel) * (Mathf.Pow(2f, moonEmissionIntensity) * Mathf.Lerp(1f, moonPartyGain, _partyLevel) * u));
                moonRenderer.SetPropertyBlock(_moonBlock);
            }

            if (operaLights != null)
            {
                if (_operaLightWasOn == null || _operaLightWasOn.Length != operaLights.Length) _operaLightWasOn = new bool[operaLights.Length];
                for (int i = 0; i < operaLights.Length; i++)
                {
                    Light l = operaLights[i];
                    if (l == null) continue;
                    float b = operaLightBases != null && i < operaLightBases.Length ? operaLightBases[i] : 1f;
                    l.intensity = b * (1f - t) * (1f - Mathf.Clamp01(operaDim));
                    // v8 : éteinte quand l'intensité tombe à 0, rallumée seulement au retour vers l'opéra ; entre les deux, un « enabled »
                    // basculé à la main (bouton Poursuites on/off du DebugPanel) est conservé.
                    bool on = l.intensity > 0.001f;
                    if (!on) l.enabled = false;
                    else if (!_operaLightWasOn[i]) l.enabled = true;
                    _operaLightWasOn[i] = on;
                }
            }

            float warm = Mathf.Lerp(0.6f, 1f, t) * Mathf.Lerp(1f, torchPartyGain, _partyLevel);
            Color flame = Color.Lerp(torchColor, torchColorParty, _partyLevel);
            SetFlames(torchLights, torchBase * warm, flame);
            SetFlames(brazierLights, brazierBase * warm, flame);
        }

        /// <summary>v9 : recolore le cubemap de réflexion uniforme (stocké en linéaire) quand la couleur change.</summary>
        void ApplyReflection(Color c)
        {
            if (_reflInstance == null) return;
            if (c == _reflColor) return;
            _reflColor = c;
            Color lin = c.linear;
            if (_reflPixels == null) _reflPixels = new Color[FlatReflectionSize * FlatReflectionSize];
            for (int i = 0; i < _reflPixels.Length; i++) _reflPixels[i] = lin;
            for (int f = 0; f < 6; f++) _reflInstance.SetPixels(_reflPixels, (CubemapFace)f);
            _reflInstance.Apply(false, false);
        }

        bool HasParameter(string name)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter p in _animator.parameters) if (p.name == name) return true;
            return false;
        }

        /// <summary>v9 : reprise sans saut d'une transition en cours. Les transitions Any State du contrôleur ne partent que des états au repos (gardées par Level) ;
        /// un déclencheur reçu en pleine transition reste donc en attente : on le consomme ici en relançant l'état inverse à l'instant miroir
        /// (Anim_ToOpera(t) = Anim_ToRave(D − t)). Un déclencheur sans objet (Reset à l'opéra ou en retour, Rave déjà en rave) est effacé pour ne pas rejouer plus tard.</summary>
        void GuardTransitions()
        {
            if (_animator == null || !_hasTriggers || !_animator.isActiveAndEnabled) return;
            if (_hasLevel) _animator.SetFloat(_levelId, rave);
            AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
            if (party > 0f && !st.IsName(raveState)) party = 0f; // v11 : Reset (trigger ou AbortSequence) → fin de la party
            float n = Mathf.Clamp01(st.normalizedTime);
            const float end = 0.999f;
            if (_animator.GetBool(_resetId))
            {
                if (st.IsName(raveState) && n < end) { _animator.ResetTrigger(_resetId); _animator.Play(toOperaState, 0, 1f - n); }
                else if (st.IsName(operaState) || st.IsName(toOperaState)) _animator.ResetTrigger(_resetId);
            }
            if (_animator.GetBool(_raveId))
            {
                if (st.IsName(toOperaState) && n < end) { _animator.ResetTrigger(_raveId); _animator.Play(raveState, 0, 1f - n); }
                else if (st.IsName(raveState)) _animator.ResetTrigger(_raveId);
            }
        }

        /// <summary>v11 : néons allumés en cascade (une bande toutes les neonCascadeStep s) quand le niveau de party dépasse neonThreshold, éteints d'un coup en dessous.</summary>
        void UpdateNeons()
        {
            if (neons == null || neons.Length == 0) return;
            bool want = _partyLevel > neonThreshold;
            if (want && !_neonsOn) { _neonsOn = true; _neonStart = Time.time; }
            if (!want && _neonsOn)
            {
                _neonsOn = false;
                foreach (GameObject g in neons) if (g != null && g.activeSelf) g.SetActive(false);
                return;
            }
            if (!_neonsOn) return;
            float elapsed = Time.time - _neonStart;
            for (int i = 0; i < neons.Length; i++)
            {
                GameObject g = neons[i];
                if (g == null || g.activeSelf) continue;
                if (elapsed >= i * neonCascadeStep) g.SetActive(true);
            }
        }

        static void SetFlames(Light[] lights, float intensity, Color color)
        {
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) { lights[i].intensity = intensity; lights[i].color = color; }
        }

        static void SetIntensity(Light[] lights, float value)
        {
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = value;
        }
    }
}
