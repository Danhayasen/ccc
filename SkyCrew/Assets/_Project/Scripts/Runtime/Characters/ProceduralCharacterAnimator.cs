using UnityEngine;

namespace SkyCrew.Characters
{
    /// <summary>
    /// ANIMACAO PROVISORIA (Fase 1). Balanca bracos/pernas e aplica "squash and stretch"
    /// direto nos ossos-objeto do modelo (Perna_E, Braco_D, Cabeca...). Sera substituida por
    /// rig + Animator na Fase 3; os parametros publicos ja seguem os nomes previstos no Animator.
    /// </summary>
    public sealed class ProceduralCharacterAnimator : MonoBehaviour
    {
        [SerializeField] private float _strideFrequency = 7f;
        [SerializeField] private float _strideAngle = 32f;

        private Transform _visual;
        private Transform _legL, _legR, _armL, _armR, _head;
        private Quaternion _legLRest, _legRRest, _armLRest, _armRRest, _headRest;
        private Vector3 _visualRestPos, _visualRestScale;
        private float _phase;
        private float _squash;
        private float _talkTime;

        public float Speed { get; set; }
        public bool Seated { get; set; }
        public bool Reacting { get; set; }
        public bool Carrying { get; set; }

        public void Bind(Transform visual)
        {
            _visual = visual;
            _visualRestPos = visual.localPosition;
            _visualRestScale = visual.localScale;
            _legL = Find(visual, "Perna_E");
            _legR = Find(visual, "Perna_D");
            _armL = Find(visual, "Braco_E");
            _armR = Find(visual, "Braco_D");
            _head = Find(visual, "Cabeca");
            if (_legL) _legLRest = _legL.localRotation;
            if (_legR) _legRRest = _legR.localRotation;
            if (_armL) _armLRest = _armL.localRotation;
            if (_armR) _armRRest = _armR.localRotation;
            if (_head) _headRest = _head.localRotation;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name || t.name.StartsWith(name + "."))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>Achatamento rapido (pouso de pulo, tropeco).</summary>
        public void Squash(float amount = 0.25f) => _squash = Mathf.Max(_squash, amount);

        public void Talk() => _talkTime = 0.6f;

        private void LateUpdate()
        {
            if (_visual == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            float moving = Mathf.Clamp01(Speed / 2.5f);
            _phase += dt * _strideFrequency * Mathf.Lerp(0.6f, 1.3f, moving);
            float swing = Mathf.Sin(_phase) * _strideAngle * moving;

            float legSit = Seated ? -80f : 0f;
            if (_legL) _legL.localRotation = _legLRest * Quaternion.Euler(legSit + swing, 0f, 0f);
            if (_legR) _legR.localRotation = _legRRest * Quaternion.Euler(legSit - swing, 0f, 0f);

            float armCarry = Carrying ? -75f : 0f;
            float panic = Reacting ? Mathf.Sin(Time.time * 22f) * 40f - 120f : 0f;
            if (_armL) _armL.localRotation = _armLRest * Quaternion.Euler(armCarry + panic - swing * 0.8f, 0f, 0f);
            if (_armR) _armR.localRotation = _armRRest * Quaternion.Euler(armCarry + panic + swing * 0.8f, 0f, 0f);

            _talkTime = Mathf.Max(0f, _talkTime - dt);
            float nod = _talkTime > 0f ? Mathf.Sin(Time.time * 18f) * 10f : 0f;
            float shiver = Reacting ? Mathf.Sin(Time.time * 35f) * 6f : 0f;
            if (_head) _head.localRotation = _headRest * Quaternion.Euler(nod, shiver, 0f);

            // pulinho ao andar + squash and stretch
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * moving;
            _squash = Mathf.MoveTowards(_squash, 0f, dt * 1.5f);
            float s = 1f - _squash + (Reacting ? Mathf.Sin(Time.time * 30f) * 0.03f : 0f);
            _visual.localScale = new Vector3(_visualRestScale.x / Mathf.Sqrt(s), _visualRestScale.y * s,
                                             _visualRestScale.z / Mathf.Sqrt(s));
            _visual.localPosition = _visualRestPos + Vector3.up * bob;
        }
    }
}
