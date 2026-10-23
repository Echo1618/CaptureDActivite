using UnityEngine;

namespace Maestro.Environment
{
    /// <summary>
    /// Balayage des spots de StageLights en party (v11) : chaque spot pivote lentement de ±sweepDegrees autour de la verticale par rapport à sa visée
    /// de base (sinus à sweepHz, phases décalées) et alterne magenta / cyan toutes les colorPeriod secondes. Désactivé par défaut, activé par
    /// RaveSequence ; à la désactivation, visées et couleurs de base sont restaurées.
    /// </summary>
    [DisallowMultipleComponent]
    public class StageLightSweep : MonoBehaviour
    {
        public float sweepDegrees = 25f;
        public float sweepHz = 0.2f;
        public float colorPeriod = 2f;
        public Color colorA = new Color(1f, 0f, 1f);
        public Color colorB = new Color(0f, 1f, 1f);

        Light[] _lights = new Light[0];
        Quaternion[] _baseRot;
        Color[] _baseColor;
        bool _ready;
        float _t;

        public int LightCount { get { return _lights.Length; } }

        void OnEnable()
        {
            if (!_ready)
            {
                _lights = GetComponentsInChildren<Light>(true);
                _baseRot = new Quaternion[_lights.Length];
                _baseColor = new Color[_lights.Length];
                for (int i = 0; i < _lights.Length; i++) { _baseRot[i] = _lights[i].transform.rotation; _baseColor[i] = _lights[i].color; }
                _ready = true;
            }
            _t = 0f;
        }

        void OnDisable()
        {
            if (!_ready) return;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null) continue;
                _lights[i].transform.rotation = _baseRot[i];
                _lights[i].color = _baseColor[i];
            }
        }

        void Update()
        {
            _t += Time.deltaTime;
            bool swap = colorPeriod > 0f && Mathf.FloorToInt(_t / colorPeriod) % 2 == 1;
            for (int i = 0; i < _lights.Length; i++)
            {
                Light l = _lights[i];
                if (l == null) continue;
                float phase = _lights.Length > 0 ? i * (2f * Mathf.PI / _lights.Length) : 0f;
                float angle = sweepDegrees * Mathf.Sin(2f * Mathf.PI * sweepHz * _t + phase);
                l.transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * _baseRot[i];
                l.color = (i % 2 == 0) != swap ? colorA : colorB;
            }
        }
    }
}
