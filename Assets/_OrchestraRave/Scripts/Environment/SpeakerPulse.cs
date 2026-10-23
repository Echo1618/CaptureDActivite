using UnityEngine;

namespace Maestro.Environment
{
    /// <summary>
    /// Pulsation d'un caisson (v11), posée par le build sur chaque Speaker_N, désactivée par défaut et activée par RaveSequence en phase Party.
    /// À chaque temps : le Model fait un squash-and-stretch (1,06 en X/Z, 0,94 en Y pendant holdSeconds, retour en releaseSeconds avec ease-out),
    /// le Cone (Model/Cone, façade vers −Z) avance de coneAdvance et revient, l'émission du cône passe de sa valeur de base à ×emissionBoost puis redescend
    /// (MaterialPropertyBlock, jamais d'instance de matériau). Horloge interne à bpm, décalée de phaseOffsetBeats temps (le build met 0, ¼, ½, ¾).
    /// Contrat devs : appeler <see cref="Beat"/> depuis l'horloge audio ; dès le premier appel, l'horloge interne se coupe (le décalage de phase ne s'applique plus).
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeakerPulse : MonoBehaviour
    {
        [Header("Horloge")]
        public float bpm = 128f;
        [Tooltip("Décalage de ce caisson, en fraction de temps (0, 0,25, 0,5, 0,75 pour les quatre caissons).")]
        public float phaseOffsetBeats = 0f;

        [Header("Enveloppe d'un temps")]
        public float squashXZ = 1.06f;
        public float squashY = 0.94f;
        public float holdSeconds = 0.08f;
        public float releaseSeconds = 0.25f;
        [Tooltip("Avance du cône vers l'avant du caisson (−Z local), en mètres.")]
        public float coneAdvance = 0.04f;
        public float emissionBoost = 2.5f;

        [Header("Hiérarchie")]
        public string modelName = "Model";
        public string coneName = "Cone";

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        Transform _model, _cone;
        Renderer _coneRenderer;
        MaterialPropertyBlock _block;
        Vector3 _modelScale = Vector3.one, _conePos;
        Color _emissionBase = Color.black;
        bool _hasEmission, _ready, _external;
        float _clock, _nextBeat, _sinceBeat = float.MaxValue, _applied = -1f;

        /// <summary>Vrai dès que Beat() a été appelé au moins une fois (horloge externe).</summary>
        public bool ExternalClock { get { return _external; } }
        /// <summary>Nombre de temps joués depuis l'activation.</summary>
        public int BeatCount { get; private set; }
        /// <summary>Enveloppe courante (1 au temps, 0 au repos).</summary>
        public float Envelope { get { return Env(_sinceBeat); } }
        public float BeatPeriod { get { return bpm > 0f ? 60f / bpm : 1f; } }
        public Transform Model { get { Resolve(); return _model; } }
        public Transform Cone { get { Resolve(); return _cone; } }

        void Awake() { Resolve(); }

        void OnEnable()
        {
            Resolve();
            _clock = 0f;
            _nextBeat = BeatPeriod * Mathf.Max(0f, phaseOffsetBeats);
            _sinceBeat = float.MaxValue;
            _external = false;
            BeatCount = 0;
            Apply(0f);
        }

        void OnDisable()
        {
            _sinceBeat = float.MaxValue;
            Apply(0f);
        }

        /// <summary>Déclenche un temps (horloge externe des devs). Coupe l'horloge interne.</summary>
        public void Beat()
        {
            _external = true;
            Trigger();
        }

        void Resolve()
        {
            if (_ready) return;
            _model = transform.Find(modelName);
            _cone = _model != null ? _model.Find(coneName) : null;
            if (_cone == null) _cone = FindDeep(transform, coneName);
            if (_model != null) _modelScale = _model.localScale;
            if (_cone != null)
            {
                _conePos = _cone.localPosition;
                _coneRenderer = _cone.GetComponent<Renderer>();
                if (_coneRenderer != null && _coneRenderer.sharedMaterial != null && _coneRenderer.sharedMaterial.HasProperty(EmissionColorId))
                {
                    _emissionBase = _coneRenderer.sharedMaterial.GetColor(EmissionColorId);
                    _hasEmission = true;
                }
            }
            _block = new MaterialPropertyBlock();
            _ready = true;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t != root && t.name == name) return t;
            return null;
        }

        void Trigger()
        {
            _sinceBeat = 0f;
            BeatCount++;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_external)
            {
                _clock += dt;
                float period = BeatPeriod;
                while (_clock >= _nextBeat) { Trigger(); _nextBeat += period; }
            }
            if (_sinceBeat < float.MaxValue) _sinceBeat += dt;
            Apply(Env(_sinceBeat));
        }

        float Env(float t)
        {
            if (t >= float.MaxValue || t < 0f) return 0f;
            if (t < holdSeconds) return 1f;
            float x = releaseSeconds > 0f ? Mathf.Clamp01((t - holdSeconds) / releaseSeconds) : 1f;
            float k = 1f - x;
            return k * k; // ease-out (décélération)
        }

        void Apply(float e)
        {
            if (Mathf.Abs(e - _applied) < 1e-4f) return;
            _applied = e;
            if (_model != null) _model.localScale = Vector3.Scale(_modelScale, new Vector3(Mathf.Lerp(1f, squashXZ, e), Mathf.Lerp(1f, squashY, e), Mathf.Lerp(1f, squashXZ, e)));
            if (_cone != null) _cone.localPosition = _conePos + new Vector3(0f, 0f, -coneAdvance * e);
            if (_coneRenderer != null && _hasEmission)
            {
                _coneRenderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, _emissionBase * Mathf.Lerp(1f, emissionBoost, e));
                _coneRenderer.SetPropertyBlock(_block);
            }
        }
    }
}
