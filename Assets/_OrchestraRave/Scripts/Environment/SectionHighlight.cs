using System.Collections.Generic;
using UnityEngine;

namespace Maestro.Environment
{
    /// <summary>
    /// Surbrillance d'une section (douce) et d'un musicien (forte), posée par le build sur la racine de chaque prefab de section
    /// (Strings, Vent, Brass, Percution). Rendu : émission URP/Lit via MaterialPropertyBlock sur les renderers de chaque Musician_N
    /// (Body et Head, ou tout renderer d'un futur personnage) ; les renderers sous l'enfant « Instrument » sont ignorés.
    /// Jamais Material.color ni instance de matériau (instancing préservé). Le bloom du volume fait le halo.
    ///
    /// Contrat devs :
    ///  - SetSectionHighlighted(bool on) : toute la section en surbrillance douce (couleur de section × softIntensity, 0,8) ;
    ///    false éteint aussi le musicien fort éventuel (pas de musicien fort sans sa section).
    ///  - SetMusicianHighlighted(int index, bool on) : Musician_&lt;index+1&gt; en surbrillance forte (couleur × 2,5 ± 0,5, sinus 1 Hz)
    ///    ET sa section en douce ; un seul musicien fort à la fois par section : en sélectionner un autre éteint le précédent.
    ///    on = false retire la surbrillance forte de ce musicien, la section reste douce.
    ///  - ClearAll() : tout éteint.
    ///  - Lecture : SectionHighlighted, HighlightedMusician (−1 : aucun), MusicianCount.
    /// Transitions en fadeSeconds (0,15 s) à l'allumage et à l'extinction, interpolées dans Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class SectionHighlight : MonoBehaviour
    {
        [Header("Couleur de la section (remplie par le build : couleur de base du matériau de section)")]
        public Color sectionColor = Color.white;

        [Header("Niveaux")]
        [Tooltip("Intensité émissive de la surbrillance douce (section entière).")]
        public float softIntensity = 0.8f;
        [Tooltip("Intensité émissive moyenne de la surbrillance forte (musicien sélectionné).")]
        public float strongIntensity = 2.5f;
        [Tooltip("Amplitude de la pulsation autour de strongIntensity (sinus) : 2,5 ± 0,5 → entre 2 et 3.")]
        public float pulseAmplitude = 0.5f;
        public float pulseHz = 1f;
        [Tooltip("Durée des transitions à l'allumage et à l'extinction.")]
        public float fadeSeconds = 0.15f;

        [Header("Hiérarchie")]
        [Tooltip("Préfixe des enfants musiciens (Musician_1, Musician_2, …), triés par numéro.")]
        public string musicianPrefix = "Musician_";
        [Tooltip("Les renderers situés sous un enfant portant ce nom ne sont pas affectés (modèles d'instruments).")]
        public string instrumentName = "Instrument";

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        const string Tag = "[SectionHighlight] ";

        class Musician
        {
            public Transform root;
            public Renderer[] renderers;
            public float level;      // 0 → 1 : part de surbrillance forte
            public float applied = -1f; // dernière intensité appliquée (−1 : jamais)
        }

        readonly List<Musician> _musicians = new List<Musician>();
        MaterialPropertyBlock _block;
        bool _sectionOn;
        int _strong = -1;
        float _sectionLevel;
        bool _dirty = true;

        public bool SectionHighlighted { get { return _sectionOn; } }
        public int HighlightedMusician { get { return _strong; } }
        public int MusicianCount { get { Ensure(); return _musicians.Count; } }

        void Awake() { Ensure(); }

        void OnDisable()
        {
            foreach (Musician m in _musicians) { m.level = 0f; Apply(m, 0f); }
            _sectionLevel = 0f;
            _dirty = true;
        }

        // ------------------------------------------------------------------ API

        public void SetSectionHighlighted(bool on)
        {
            Ensure();
            _sectionOn = on;
            if (!on) _strong = -1;
            _dirty = true;
        }

        public void SetMusicianHighlighted(int index, bool on)
        {
            Ensure();
            if (index < 0 || index >= _musicians.Count)
            {
                Debug.LogWarning(Tag + name + " : index " + index + " hors de [0, " + (_musicians.Count - 1) + "].");
                return;
            }
            if (on) { _strong = index; _sectionOn = true; }
            else if (_strong == index) _strong = -1;
            _dirty = true;
        }

        public void ClearAll()
        {
            _sectionOn = false;
            _strong = -1;
            _dirty = true;
        }

        // ------------------------------------------------------------------ interne

        void Ensure()
        {
            if (_musicians.Count > 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith(musicianPrefix)) continue;
                var rs = new List<Renderer>();
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                    if (!IsUnderInstrument(r.transform, child)) rs.Add(r);
                _musicians.Add(new Musician { root = child, renderers = rs.ToArray() });
            }
            _musicians.Sort((a, b) => Number(a.root.name).CompareTo(Number(b.root.name)));
        }

        int Number(string n)
        {
            int v;
            return int.TryParse(n.Substring(musicianPrefix.Length), out v) ? v : int.MaxValue;
        }

        bool IsUnderInstrument(Transform t, Transform stop)
        {
            for (Transform p = t; p != null && p != stop; p = p.parent)
                if (p.name == instrumentName) return true;
            return false;
        }

        void Update()
        {
            if (_musicians.Count == 0) return;
            float sectionTarget = _sectionOn ? 1f : 0f;
            bool active = _dirty || _strong >= 0 || _sectionLevel != sectionTarget;
            if (!active)
                foreach (Musician m in _musicians) if (m.level > 0f) { active = true; break; }
            if (!active) return;
            _dirty = false;

            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            _sectionLevel = Mathf.MoveTowards(_sectionLevel, sectionTarget, step);
            float pulse = strongIntensity + pulseAmplitude * Mathf.Sin(2f * Mathf.PI * pulseHz * Time.time);
            for (int i = 0; i < _musicians.Count; i++)
            {
                Musician m = _musicians[i];
                m.level = Mathf.MoveTowards(m.level, _strong == i ? 1f : 0f, step);
                Apply(m, Mathf.Lerp(softIntensity * _sectionLevel, pulse, m.level));
            }
        }

        void Apply(Musician m, float intensity)
        {
            if (Mathf.Abs(intensity - m.applied) < 1e-4f) return;
            m.applied = intensity;
            if (_block == null) _block = new MaterialPropertyBlock();
            Color c = sectionColor * intensity;
            c.a = 1f;
            foreach (Renderer r in m.renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
