using UnityEngine;

namespace SkyCrew.Characters
{
    /// <summary>O que os controladores (jogador, passageiro) pedem para a animacao.</summary>
    public interface ICharacterAnimation
    {
        float Speed { get; set; }
        bool Seated { get; set; }
        bool Reacting { get; set; }
        bool Carrying { get; set; }

        /// <summary>Gesto de fala (uma vez).</summary>
        void Talk();

        /// <summary>Escorregao/tropeco (uma vez).</summary>
        void Slip();

        /// <summary>Achatamento rapido (pouso, susto).</summary>
        void Squash(float amount = 0.25f);
    }

    /// <summary>Escolhe a melhor animacao disponivel para um visual.</summary>
    public static class CharacterAnimation
    {
        /// <summary>Controller gerado pelo menu "SkyCrew > 5. Gerar Animator dos personagens".</summary>
        public const string ControllerResource = "SkyCrew/Animacao/Personagem";

        private static RuntimeAnimatorController _controller;
        private static bool _searched;

        public static RuntimeAnimatorController Controller
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _controller = Resources.Load<RuntimeAnimatorController>(ControllerResource);
                    if (_controller == null)
                    {
                        Debug.LogWarning("[SkyCrew] Animator dos personagens nao encontrado; usando animacao procedural. " +
                                         "Rode 'SkyCrew > 5. Gerar Animator dos personagens'.");
                    }
                }

                return _controller;
            }
        }

        /// <summary>
        /// Anexa ao <paramref name="owner"/> a animacao do visual: Animator com esqueleto (Fase 3) se o modelo
        /// tiver Animator e o controller existir; senao, a animacao procedural provisoria.
        /// </summary>
        public static ICharacterAnimation Attach(GameObject owner, Transform visual)
        {
            var animator = visual.GetComponentInChildren<Animator>();
            if (animator != null && Controller != null)
            {
                var driver = owner.AddComponent<AnimatorCharacterAnimation>();
                driver.Bind(animator, Controller, visual);
                return driver;
            }

            var procedural = owner.AddComponent<ProceduralCharacterAnimator>();
            procedural.Bind(visual);
            return procedural;
        }
    }

    /// <summary>
    /// Dirige o Animator (parametros Speed, Seated, Carrying, Reacting e gatilhos Talk, Slip)
    /// e mantem o "squash and stretch" cartunesco por cima da animacao.
    /// </summary>
    public sealed class AnimatorCharacterAnimation : MonoBehaviour, ICharacterAnimation
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int SeatedId = Animator.StringToHash("Seated");
        public static readonly int CarryingId = Animator.StringToHash("Carrying");
        public static readonly int ReactingId = Animator.StringToHash("Reacting");
        public static readonly int TalkId = Animator.StringToHash("Talk");
        public static readonly int SlipId = Animator.StringToHash("Slip");

        private Animator _animator;
        private Transform _visual;
        private Vector3 _baseScale;
        private float _squash;

        public float Speed { get; set; }
        public bool Seated { get; set; }
        public bool Reacting { get; set; }
        public bool Carrying { get; set; }
        public Animator Animator => _animator;

        public void Bind(Animator animator, RuntimeAnimatorController controller, Transform visual)
        {
            _animator = animator;
            _animator.runtimeAnimatorController = controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            _visual = visual;
            _baseScale = visual.localScale;
        }

        public void Talk() => _animator.SetTrigger(TalkId);

        public void Slip()
        {
            _animator.SetTrigger(SlipId);
            Squash(0.2f);
        }

        public void Squash(float amount = 0.25f) => _squash = Mathf.Max(_squash, amount);

        private void Update()
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(SpeedId, Speed, 0.1f, Time.deltaTime);
            _animator.SetBool(SeatedId, Seated);
            _animator.SetBool(CarryingId, Carrying);
            _animator.SetBool(ReactingId, Reacting);
        }

        private void LateUpdate()
        {
            _squash = Mathf.MoveTowards(_squash, 0f, Time.deltaTime * 1.5f);
            float s = 1f - _squash;
            _visual.localScale = new Vector3(_baseScale.x / Mathf.Sqrt(s), _baseScale.y * s, _baseScale.z / Mathf.Sqrt(s));
        }
    }
}
