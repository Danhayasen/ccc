using SkyCrew.Physics;
using UnityEngine;

namespace SkyCrew.Player
{
    /// <summary>
    /// Movimento do jogador com CharacterController (decisao em Docs/TECHNICAL_DECISIONS.md):
    /// previsivel em corredor estreito, sem jitter de fisica e independente da taxa de quadros.
    /// Empurra objetos fisicos com forca limitada e pode "perder o controle" por instantes
    /// (tropeco na turbulencia), recuperando sozinho.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Velocidades (m/s)")]
        [SerializeField] private float _walkSpeed = 2.6f;
        [SerializeField] private float _runSpeed = 4.2f;
        [SerializeField] private float _acceleration = 14f;
        [SerializeField] private float _deceleration = 18f;
        [SerializeField] private float _turnSpeed = 720f;

        [Header("Pulo e gravidade")]
        [SerializeField] private bool _allowJump = true;
        [SerializeField] private float _jumpHeight = 0.6f;
        [SerializeField] private float _gravity = -20f;

        [Header("Empurrar objetos")]
        [SerializeField] private float _pushStrength = 1.5f;

        private CharacterController _controller;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _stumbleTime;
        private Vector3 _stumbleVelocity;
        private bool _wasGrounded = true;

        public Vector3 Velocity => _planarVelocity + Vector3.up * _verticalVelocity;
        public bool IsGrounded => _controller.isGrounded;
        public bool IsStumbling => _stumbleTime > 0f;
        public float PlanarSpeed => _planarVelocity.magnitude;

        /// <summary>Disparado ao tocar o chao depois de cair (para squash na animacao).</summary>
        public event System.Action Landed;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _controller.radius = 0.25f;
            _controller.height = 1.6f;
            _controller.center = new Vector3(0f, 0.8f, 0f);
            _controller.stepOffset = 0.25f;
            _controller.slopeLimit = 45f;
            _controller.skinWidth = 0.03f;
            _controller.minMoveDistance = 0f;
        }

        /// <param name="moveInput">Entrada (x = lado, y = frente), magnitude ate 1.</param>
        /// <param name="cameraYaw">Rotacao horizontal da camera (movimento relativo a ela).</param>
        public void Step(Vector2 moveInput, float cameraYaw, bool sprint, bool jumpPressed, float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            Vector3 desired = Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);
            float targetSpeed = sprint ? _runSpeed : _walkSpeed;
            Vector3 targetVelocity = desired * targetSpeed;

            if (_stumbleTime > 0f)
            {
                // Perda de controle temporaria: segue o empurrao e recupera.
                _stumbleTime -= dt;
                targetVelocity = _stumbleVelocity;
                _stumbleVelocity = Vector3.MoveTowards(_stumbleVelocity, Vector3.zero, 6f * dt);
            }

            float rate = targetVelocity.sqrMagnitude > _planarVelocity.sqrMagnitude ? _acceleration : _deceleration;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, rate * dt);

            if (desired.sqrMagnitude > 0.001f && _stumbleTime <= 0f)
            {
                var look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * dt);
            }

            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f; // mantem colado ao piso
            }

            if (grounded && jumpPressed && _allowJump && _stumbleTime <= 0f)
            {
                _verticalVelocity = Mathf.Sqrt(2f * -_gravity * _jumpHeight);
            }

            _verticalVelocity += _gravity * dt;
            _controller.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);

            bool nowGrounded = _controller.isGrounded;
            if (nowGrounded && !_wasGrounded)
            {
                Landed?.Invoke();
            }

            _wasGrounded = nowGrounded;
        }

        /// <summary>Tropeco: empurrao horizontal e perda de controle por alguns instantes.</summary>
        public void Stumble(Vector3 push, float seconds)
        {
            push.y = 0f;
            _stumbleVelocity = Vector3.ClampMagnitude(push, 3f);
            _stumbleTime = Mathf.Clamp(seconds, 0f, 1.5f);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _controller.enabled = true;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var body = hit.rigidbody;
            if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f)
            {
                return;
            }

            var item = body.GetComponent<CarryableItem>();
            Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z) * (_pushStrength * PlanarSpeed);
            if (item != null)
            {
                item.ApplyImpulse(push * Time.deltaTime * 10f, Vector3.zero);
            }
        }
    }
}
