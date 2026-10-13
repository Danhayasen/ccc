using SkyCrew.Characters;
using SkyCrew.Interaction;
using SkyCrew.Physics;
using UnityEngine;

namespace SkyCrew.Player
{
    /// <summary>
    /// Liga entrada -> movimento/camera/interacao/animacao. Unica autoridade de movimento
    /// do personagem local (no multiplayer, a entrada vira comando de rede).
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        private IPlayerCommandSource _input;
        private PlayerMotor _motor;
        private ThirdPersonCamera _camera;
        private Interactor _interactor;
        private CarrySocket _hands;
        private ICharacterAnimation _animator;

        public bool ControlsEnabled { get; set; } = true;
        public PlayerMotor Motor => _motor;
        public Interactor Interactor => _interactor;
        public ThirdPersonCamera Camera => _camera;

        public void Initialize(IPlayerCommandSource input, ThirdPersonCamera cameraRig, ICharacterAnimation animator)
        {
            _input = input;
            _camera = cameraRig;
            _animator = animator;
            _motor = GetComponent<PlayerMotor>();
            _interactor = GetComponent<Interactor>();
            _hands = GetComponent<CarrySocket>();
            _motor.Landed += () => _animator?.Squash(0.3f);
            _motor.Stumbled += () => _animator?.Slip();
        }

        private void Update()
        {
            if (_input == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool active = ControlsEnabled && Time.timeScale > 0f;
            Vector2 move = active ? _input.Move : Vector2.zero;
            if (active)
            {
                _camera.Rotate(_input.Look);
            }

            _motor.Step(move, _camera.Yaw, active && _input.Sprint, active && _input.JumpPressed, dt);

            if (active && _input.InteractPressed)
            {
                _interactor.TryInteract();
                _animator?.Talk();
            }

            if (_animator != null)
            {
                _animator.Speed = _motor.PlanarSpeed;
                _animator.Carrying = _hands.Held != null;
            }
        }
    }
}
