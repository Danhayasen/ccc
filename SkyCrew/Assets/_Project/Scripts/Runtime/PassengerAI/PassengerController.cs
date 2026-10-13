using SkyCrew.Aircraft;
using SkyCrew.Characters;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace SkyCrew.PassengerAI
{
    /// <summary>
    /// "Corpo" do passageiro: executa os comandos do <see cref="PassengerBrain"/> (dominio)
    /// com NavMeshAgent, entra/sai da poltrona e informa chegada, falha de rota e travamento.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PassengerController : MonoBehaviour
    {
        private enum BodyMode
        {
            Seated,
            LeavingSeat,
            WaitingForAisle,
            Walking,
            EnteringSeat,
            Standing,
        }

        private const float SlideSeconds = 0.6f;
        private const float StuckSeconds = 3f;

        private NavMeshAgent _agent;
        private AisleTrafficCoordinator _traffic;
        private ProceduralCharacterAnimator _animator;
        private BodyMode _mode = BodyMode.Standing;
        private DestinationKind _destination;
        private Vector3 _seatPosition;
        private Vector3 _aislePoint;
        private Vector3 _lavatoryPoint;
        private Vector3 _slideFrom, _slideTo;
        private float _slideT;
        private float _stuckTimer;
        private Vector3 _lastPosition;

        public PassengerBrain Brain { get; private set; }
        public string DisplayName { get; private set; }
        public string SeatId { get; private set; }
        public ServiceKind ShownRequest { get; private set; }
        public bool IsReacting { get; private set; }
        public string ModeName => _mode.ToString();

        public void Initialize(PassengerBrain brain, string displayName, string seatId, Vector3 lavatoryPoint,
                               AisleTrafficCoordinator traffic, ProceduralCharacterAnimator animator)
        {
            Brain = brain;
            DisplayName = displayName;
            SeatId = seatId;
            _traffic = traffic;
            _animator = animator;
            _lavatoryPoint = lavatoryPoint;
            CabinLayout.TryParseSeat(seatId, out int row, out int letter);
            _seatPosition = CabinLayout.SeatPosition(row, letter);
            _aislePoint = CabinLayout.AislePointForRow(row);

            _agent = GetComponent<NavMeshAgent>();
            _agent.radius = CabinNavMesh.AgentRadius;
            _agent.height = CabinNavMesh.AgentHeight;
            _agent.speed = brain.Profile.WalkSpeed;
            _agent.acceleration = 8f;
            _agent.angularSpeed = 540f;
            _agent.stoppingDistance = 0.1f;
            _agent.autoBraking = true;
            // Personalidades mais "apressadas" tem prioridade no corredor (menor = mais importante).
            _agent.avoidancePriority = Mathf.Clamp(60 - Mathf.RoundToInt(brain.Profile.WalkSpeed * 10f), 1, 99);
            _agent.enabled = false;
        }

        private void Update()
        {
            if (Brain == null)
            {
                return;
            }

            while (Brain.TryDequeueCommand(out var command))
            {
                Execute(command);
            }

            float dt = Time.deltaTime;
            switch (_mode)
            {
                case BodyMode.LeavingSeat:
                case BodyMode.EnteringSeat:
                    UpdateSlide(dt);
                    break;
                case BodyMode.WaitingForAisle:
                    if (_traffic.Request(this))
                    {
                        BeginNavigation();
                    }

                    break;
                case BodyMode.Walking:
                    UpdateWalking(dt);
                    break;
            }

            if (_animator != null)
            {
                _animator.Speed = _agent.enabled ? _agent.velocity.magnitude : 0f;
                _animator.Seated = _mode == BodyMode.Seated;
                _animator.Reacting = IsReacting;
            }
        }

        private void Execute(PassengerCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.WalkTo:
                    _destination = command.Destination;
                    if (_mode == BodyMode.Seated)
                    {
                        StartSlide(_seatPosition, _aislePoint, BodyMode.LeavingSeat);
                    }
                    else
                    {
                        _mode = BodyMode.WaitingForAisle;
                    }

                    break;

                case CommandKind.StopMoving:
                    if (_agent.enabled && _agent.isOnNavMesh)
                    {
                        _agent.isStopped = true;
                    }

                    _traffic.Release(this);
                    if (_mode != BodyMode.Seated)
                    {
                        _mode = BodyMode.Standing;
                    }

                    break;

                case CommandKind.SnapToSeat:
                    _traffic.Release(this);
                    _agent.enabled = false;
                    transform.SetPositionAndRotation(_seatPosition, Quaternion.identity);
                    _mode = BodyMode.Seated;
                    break;

                case CommandKind.ShowRequest:
                    ShownRequest = command.Service;
                    break;

                case CommandKind.ClearRequest:
                    ShownRequest = ServiceKind.None;
                    _animator?.Talk();
                    break;

                case CommandKind.React:
                    IsReacting = true;
                    _animator?.Squash(0.2f);
                    break;

                case CommandKind.StopReacting:
                    IsReacting = false;
                    break;
            }
        }

        private void StartSlide(Vector3 from, Vector3 to, BodyMode mode)
        {
            _agent.enabled = false;
            _slideFrom = from;
            _slideTo = to;
            _slideT = 0f;
            _mode = mode;
        }

        private void UpdateSlide(float dt)
        {
            _slideT += dt / SlideSeconds;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_slideT));
            transform.position = Vector3.Lerp(_slideFrom, _slideTo, k);
            if (_slideT < 1f)
            {
                return;
            }

            if (_mode == BodyMode.LeavingSeat)
            {
                _mode = BodyMode.WaitingForAisle;
            }
            else
            {
                _mode = BodyMode.Seated;
                Brain.OnArrived();
            }
        }

        private void BeginNavigation()
        {
            _agent.enabled = true;
            if (!_agent.Warp(transform.position) || !_agent.isOnNavMesh)
            {
                _traffic.Release(this);
                _mode = BodyMode.Standing;
                Brain.OnNavigationFailed();
                return;
            }

            _agent.isStopped = false;
            Vector3 target = _destination == DestinationKind.Lavatory ? _lavatoryPoint : _aislePoint;
            if (!_agent.SetDestination(target))
            {
                _traffic.Release(this);
                _mode = BodyMode.Standing;
                Brain.OnNavigationFailed();
                return;
            }

            _mode = BodyMode.Walking;
            _stuckTimer = 0f;
            _lastPosition = transform.position;
        }

        private void UpdateWalking(float dt)
        {
            if (_agent.pathPending)
            {
                return;
            }

            if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Fail();
                return;
            }

            if (_agent.remainingDistance <= _agent.stoppingDistance + 0.05f)
            {
                _traffic.Release(this);
                if (_destination == DestinationKind.Seat)
                {
                    StartSlide(transform.position, _seatPosition, BodyMode.EnteringSeat);
                }
                else
                {
                    _mode = BodyMode.Standing;
                    Brain.OnArrived();
                }

                return;
            }

            // Deteccao de travamento.
            _stuckTimer += dt;
            if (_stuckTimer >= StuckSeconds)
            {
                bool moved = (transform.position - _lastPosition).sqrMagnitude > 0.05f * 0.05f;
                _stuckTimer = 0f;
                _lastPosition = transform.position;
                if (!moved)
                {
                    Fail();
                }
            }
        }

        private void Fail()
        {
            _traffic.Release(this);
            _mode = BodyMode.Standing;
            Brain.OnNavigationFailed();
        }

        private void OnDisable()
        {
            _traffic?.Release(this);
        }
    }
}
