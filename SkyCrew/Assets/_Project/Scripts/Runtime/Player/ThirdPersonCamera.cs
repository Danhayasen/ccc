using UnityEngine;

namespace SkyCrew.Player
{
    /// <summary>
    /// Camera orbital em terceira pessoa com limites de inclinacao, sensibilidade,
    /// colisao (SphereCast) para nao atravessar paredes e tremor para eventos.
    /// </summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _pivotOffset = new Vector3(0f, 1.45f, 0f);
        [SerializeField] private float _distance = 2.6f;
        [SerializeField] private float _minDistance = 0.35f;
        [SerializeField] private float _minPitch = -35f;
        [SerializeField] private float _maxPitch = 70f;
        [SerializeField] private float _collisionRadius = 0.18f;
        [SerializeField] private float _followSharpness = 18f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        private float _yaw;
        private float _pitch = 15f;
        private float _currentDistance;
        private float _shake;

        public float Sensitivity { get; set; } = 1f;
        public bool InvertY { get; set; }
        public float Yaw => _yaw;

        public void SetTarget(Transform target, float initialYaw)
        {
            _target = target;
            _yaw = initialYaw;
            _currentDistance = _distance;
            Snap();
        }

        public void AddShake(float amount) => _shake = Mathf.Clamp(_shake + amount, 0f, 0.6f);

        public void Rotate(Vector2 look)
        {
            _yaw += look.x * Sensitivity;
            _pitch += (InvertY ? look.y : -look.y) * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        private void Snap()
        {
            if (_target == null)
            {
                return;
            }

            Place(1f);
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Place(1f - Mathf.Exp(-_followSharpness * Time.unscaledDeltaTime));
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 0.8f);
        }

        private void Place(float blend)
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _target.position + _pivotOffset;
            Vector3 back = rotation * Vector3.back;

            float wanted = _distance;
            if (UnityEngine.Physics.SphereCast(pivot, _collisionRadius, back, out var hit, _distance, _collisionMask,
                                               QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != _target.root)
            {
                wanted = Mathf.Max(_minDistance, hit.distance - 0.05f);
            }

            // Aproxima na hora (evita atravessar); afasta suavemente.
            _currentDistance = wanted < _currentDistance ? wanted : Mathf.Lerp(_currentDistance, wanted, blend);

            Vector3 shakeOffset = _shake > 0f
                ? new Vector3(Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f, 0f) * _shake
                : Vector3.zero;
            transform.SetPositionAndRotation(pivot + back * _currentDistance + rotation * shakeOffset, rotation);
        }
    }
}
