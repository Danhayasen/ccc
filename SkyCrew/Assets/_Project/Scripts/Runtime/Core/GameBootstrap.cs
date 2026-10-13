using System.Collections.Generic;
using SkyCrew.Aircraft;
using SkyCrew.Characters;
using SkyCrew.Data;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Match;
using SkyCrew.Events;
using SkyCrew.Interaction;
using SkyCrew.Navigation;
using SkyCrew.PassengerAI;
using SkyCrew.Physics;
using SkyCrew.Player;
using SkyCrew.UI;
using UnityEngine;
using UnityEngine.AI;

namespace SkyCrew.Core
{
    /// <summary>
    /// Ponto de entrada da cena do prototipo. Cria a sessao (regras), monta o cenario,
    /// gera a navegacao, cria jogador/passageiros/objetos e conduz o ciclo da partida.
    /// Unico objeto que precisa existir na cena.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string StepLevel = "cenario";
        private const string StepNavMesh = "navmesh";
        private const string StepPlayer = "jogador";
        private const string StepPassengers = "passageiros";

        [SerializeField] private MatchConfigAsset _config;
        [SerializeField] private ulong _seed = 12345;
        [SerializeField] private float _readySeconds = 2f;
        [SerializeField] private int _maxAisleWalkers = 2;

        private readonly List<PassengerController> _passengers = new List<PassengerController>();
        private LevelRefs _level;
        private Transform _dynamicRoot;
        private PlayerController _player;
        private PlayerInputReader _input;
        private ThirdPersonCamera _cameraRig;
        private CabinNavMesh _navMesh;
        private AisleTrafficCoordinator _traffic;
        private TurbulenceDirector _turbulence;
        private GameHud _hud;
        private float _readyTimer;
        private bool _restartRequested;

        /// <summary>Sessao atual (regras do jogo). null fora da cena do prototipo.</summary>
        public static MatchSession Session { get; private set; }

        public static PrototypeScenario Scenario { get; private set; }
        public static GameBootstrap Instance { get; private set; }

        public IReadOnlyList<PassengerController> Passengers => _passengers;
        public PlayerController Player => _player;
        public LevelRefs Level => _level;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            var config = _config != null ? _config.ToDomain() : PrototypeScenario.CreateConfig(_seed);
            Session = new MatchSession(config);
            Scenario = PrototypeScenario.Install(Session);
            Session.RegisterInitStep(StepLevel);
            Session.RegisterInitStep(StepNavMesh);
            Session.RegisterInitStep(StepPlayer);
            Session.RegisterInitStep(StepPassengers);
        }

        private void Start()
        {
            _level = PrototypeLevelBuilder.Build(transform);
            Session.CompleteInitStep(StepLevel);

            _navMesh = gameObject.AddComponent<CabinNavMesh>();
            if (!_navMesh.Build(CabinLayout.NavBounds))
            {
                Session.State.TryTransition(MatchState.Failed, out _);
                return;
            }

            Session.CompleteInitStep(StepNavMesh);
            _traffic = new AisleTrafficCoordinator(_maxAisleWalkers);

            SpawnPlayer();
            Session.CompleteInitStep(StepPlayer);

            _dynamicRoot = new GameObject("Dinamicos").transform;
            _dynamicRoot.SetParent(transform, false);
            SpawnDynamicContent();
            Session.CompleteInitStep(StepPassengers); // ultimo passo -> Ready

            _turbulence = gameObject.AddComponent<TurbulenceDirector>();
            _turbulence.Bind(Session, Scenario, _cameraRig, _player.Motor, _level);

            _hud = gameObject.AddComponent<GameHud>();
            _hud.Bind(this, _player, _input, _cameraRig.GetComponent<Camera>());
        }

        private void SpawnPlayer()
        {
            var go = new GameObject("Comissaria");
            go.transform.SetPositionAndRotation(CabinLayout.PlayerSpawn, Quaternion.Euler(0f, 180f, 0f));
            go.AddComponent<CharacterController>();
            go.AddComponent<PlayerMotor>();
            var hands = go.AddComponent<CarrySocket>();
            var anchor = new GameObject("Maos").transform;
            anchor.SetParent(go.transform, false);
            anchor.localPosition = new Vector3(0f, 1.05f, 0.38f);
            hands.SetAnchor(anchor);
            go.AddComponent<Interactor>();
            _input = go.AddComponent<PlayerInputReader>();

            var visual = CharacterFactory.SpawnVisual(CharacterFactory.Crew, go.transform);
            var animator = go.AddComponent<ProceduralCharacterAnimator>();
            animator.Bind(visual);

            var camGo = new GameObject("Camera_Principal");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 65f;
            camGo.AddComponent<AudioListener>();
            _cameraRig = camGo.AddComponent<ThirdPersonCamera>();
            _cameraRig.SetTarget(go.transform, 180f);

            _player = go.AddComponent<PlayerController>();
            _player.Initialize(_input, _cameraRig, animator);
        }

        private void SpawnDynamicContent()
        {
            _passengers.Clear();
            int variant = 0;
            foreach (var brain in Session.Passengers)
            {
                var spec = Scenario.GetSpec(brain.Id);
                var go = new GameObject("Passageiro_" + spec.Name);
                go.transform.SetParent(_dynamicRoot, false);
                if (!spec.StartsSeated)
                {
                    go.transform.position = CabinLayout.BoardingDoor;
                }

                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.8f, 0f);
                capsule.height = 1.6f;
                capsule.radius = 0.24f;
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                go.AddComponent<NavMeshAgent>();
                var controller = go.AddComponent<PassengerController>();
                go.AddComponent<PassengerInteractable>();
                var visual = CharacterFactory.SpawnVisual(
                    CharacterFactory.PassengerVariants[variant % CharacterFactory.PassengerVariants.Length], go.transform, variant);
                var animator = go.AddComponent<ProceduralCharacterAnimator>();
                animator.Bind(visual);
                controller.Initialize(brain, spec.Name, spec.SeatId, CabinLayout.LavatoryPoint, _traffic, animator);
                _passengers.Add(controller);
                variant++;
            }

            // Mala esquecida no corredor (tarefa "guardar a mala")
            ItemFactory.Create(ItemTypesUnity.Suitcase, CabinLayout.ForgottenSuitcase, Quaternion.Euler(0f, 90f, 0f));
            _level.Dispenser.Configure("água",
                () => ItemFactory.Create(ItemTypesUnity.Water, _level.Dispenser.transform.position, Quaternion.identity));
        }

        private void Update()
        {
            if (Session == null)
            {
                return;
            }

            if (_restartRequested)
            {
                _restartRequested = false;
                Restart();
                return;
            }

            if (Session.State.Current == MatchState.Ready)
            {
                _readyTimer += Time.deltaTime;
                if (_readyTimer >= _readySeconds)
                {
                    _readyTimer = 0f;
                    Session.StartBoarding(out _);
                }
            }

            Session.Tick(Time.deltaTime);
        }

        /// <summary>Pedido vindo da UI; executado no proximo Update (fora do OnGUI).</summary>
        public void RequestRestart() => _restartRequested = true;

        private void Restart()
        {
            if (Session.State.IsPlayable)
            {
                // Reiniciar no meio do voo encerra a viagem atual como derrota (regra de transicao).
                Session.Finish(false, "Viagem reiniciada pelo jogador");
            }

            _player.GetComponent<CarrySocket>().Take(); // maos vazias antes de destruir os itens
            if (!Session.Reset(out string reason))
            {
                Debug.LogWarning("[SkyCrew] Reinicio recusado: " + reason);
                return;
            }

            Time.timeScale = 1f;
            Session.RegisterInitStep(StepPlayer);
            Session.RegisterInitStep(StepPassengers);

            // Nada da partida anterior permanece: objetos soltos, passageiros e itens.
            foreach (var item in Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
            {
                Destroy(item.gameObject);
            }

            Destroy(_dynamicRoot.gameObject);
            _dynamicRoot = new GameObject("Dinamicos").transform;
            _dynamicRoot.SetParent(transform, false);
            _player.Motor.Teleport(CabinLayout.PlayerSpawn, Quaternion.Euler(0f, 180f, 0f));
            Session.CompleteInitStep(StepPlayer);
            SpawnDynamicContent();
            Session.CompleteInitStep(StepPassengers);
        }

        public void QuitToMenu()
        {
            Session.State.TryTransition(MatchState.ReturningToMenu, out _);
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Session = null;
                Scenario = null;
            }
        }
    }
}
