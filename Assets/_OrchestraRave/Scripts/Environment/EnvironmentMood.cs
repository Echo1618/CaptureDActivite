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

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        Material _skyInstance;
        MaterialPropertyBlock _moonBlock;

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
            _moonBlock = new MaterialPropertyBlock();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fogMode = FogMode.Exponential;
            Apply(rave);
        }

        void LateUpdate()
        {
            Apply(rave);
        }

        void OnDestroy()
        {
            if (_skyInstance != null) Destroy(_skyInstance);
        }

        /// <summary>Applique l'interpolation opéra (0) → nuit (1). Appelé chaque frame avec <see cref="rave"/>.</summary>
        public void Apply(float t)
        {
            t = Mathf.Clamp01(t);

            if (directionalLight != null)
            {
                directionalLight.color = moonLightColor;
                directionalLight.intensity = Mathf.Lerp(0f, moonLightIntensity, t);
                directionalLight.enabled = directionalLight.intensity > 0.001f;
            }

            RenderSettings.ambientLight = Color.Lerp(ambientOpera, ambientNight, t);
            float density = Mathf.Lerp(0f, fogDensityNight, t);
            RenderSettings.fogColor = fogNight;
            RenderSettings.fogDensity = density;
            RenderSettings.fog = density > 0.00001f;

            if (moonRenderer != null)
            {
                float u = Mathf.InverseLerp(0.5f, 1f, t); // échelle 0 tant que rave < 0.5
                moonRenderer.transform.localScale = Vector3.one * (moonMaxScale * u);
                if (_moonBlock == null) _moonBlock = new MaterialPropertyBlock();
                moonRenderer.GetPropertyBlock(_moonBlock);
                _moonBlock.SetColor(EmissionColorId, moonEmissionColor * (Mathf.Pow(2f, moonEmissionIntensity) * u));
                moonRenderer.SetPropertyBlock(_moonBlock);
            }

            if (operaLights != null)
            {
                for (int i = 0; i < operaLights.Length; i++)
                {
                    Light l = operaLights[i];
                    if (l == null) continue;
                    float b = operaLightBases != null && i < operaLightBases.Length ? operaLightBases[i] : 1f;
                    l.intensity = b * (1f - t);
                    l.enabled = l.intensity > 0.001f;
                }
            }

            float warm = Mathf.Lerp(0.6f, 1f, t);
            SetIntensity(torchLights, torchBase * warm);
            SetIntensity(brazierLights, brazierBase * warm);
        }

        static void SetIntensity(Light[] lights, float value)
        {
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = value;
        }
    }
}
