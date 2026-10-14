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
        private WorldDirector _world;
        private GameHud _hud;
        private float _readyTimer;
        private bool _restartRequested;
        private bool _restartToMenu;
        private bool _autoStartNext;
        private string _matchId;
        private SkyCrew.Audio.AudioDirector _audio;
        private Transform _playerVisual;

        /// <summary>Testes e "Jogar de novo": comeca o embarque sem passar pelo menu principal.</summary>
        public static bool AutoStart { get; set; }

        /// <summary>Menu principal visivel (antes do embarque).</summary>
        public bool MenuOpen => Session != null && Session.State.Current == MatchState.Ready && !(AutoStart || _autoStartNext);

        public SkyCrew.Audio.AudioDirector Audio => _audio;

        /// <summary>Sessao atual (regras do jogo). null fora da cena do prototipo.</summary>
        public static MatchSession Session { get; private set; }

        public static PrototypeScenario Scenario { get; private set; }
        public static GameBootstrap Instance { get; private set; }

        /// <summary>Progresso permanente (carregado ao iniciar; salvo ao fim de cada partida).</summary>
        public SaveManager Save { get; private set; }

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

            Save = SaveManager.CreateDefault();
            // Cada viagem tem um id; o resultado so e contabilizado uma vez por id.
            Session.State.Changed += (previous, next) =>
            {
                if (next == MatchState.Boarding)
                {
                    _matchId = System.Guid.NewGuid().ToString("N");
                }
            };
            Session.ResultsReady += results =>
            {
                if (_matchId != null)
                {
                    Save.ApplyMatch(results, _matchId);
                }
            };
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
            _world = gameObject.AddComponent<WorldDirector>();
            _world.Bind(this, Scenario);

            _hud = gameObject.AddComponent<GameHud>();
            _hud.Bind(this, _player, _input, _cameraRig.GetComponent<Camera>());
            gameObject.AddComponent<MenuScreens>().Bind(this, _hud);

            _audio = new GameObject("Audio").AddComponent<SkyCrew.Audio.AudioDirector>();
            _audio.transform.SetParent(transform, false);
            _audio.Bind(Session, Scenario, _player.Motor, _player.Interactor, Save.Data.Settings);
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

            _playerVisual = OutfitApplier.Dress(go.transform, Save.Data.EquippedUniform, Save.Data.EquippedAccessory);
            var animator = CharacterAnimation.Attach(go, _playerVisual);

            var camGo = new GameObject("Camera_Principal");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 65f;
            camGo.AddComponent<AudioListener>();
            _cameraRig = camGo.AddComponent<ThirdPersonCamera>();
            _cameraRig.SetTarget(go.transform, 180f);
            _cameraRig.Sensitivity = Save.Data.Settings.MouseSensitivity;
            _cameraRig.InvertY = Save.Data.Settings.InvertY;
            AudioListener.volume = 1f; // volumes por canal ficam no AudioDirector

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
                var animator = CharacterAnimation.Attach(go, visual);
                controller.Initialize(brain, spec.Name, spec.SeatId, CabinLayout.LavatoryPoint, _traffic, animator);
                _passengers.Add(controller);
                variant++;
            }

            // Mala esquecida no corredor (tarefa "guardar a mala" exige esta instancia)
            ItemFactory.Create(ItemTypesUnity.Suitcase, CabinLayout.ForgottenSuitcase, Quaternion.Euler(0f, 90f, 0f),
                               PrototypeScenario.ForgottenSuitcaseId);
            ConfigureDispenser(_level.Dispenser, "água", ItemTypesUnity.Water, PrototypeScenario.EquipmentFridge,
                               _level.DispenserBrokenMarker);
            ConfigureDispenser(_level.SnackDispenser, "lanche", ItemTypesUnity.Snack);
            ConfigureDispenser(_level.BlanketDispenser, "cobertor", ItemTypesUnity.Blanket);
            ConfigureDispenser(_level.MopDispenser, "esfregão", ItemTypesUnity.Mop);
        }

        private static void ConfigureDispenser(ItemDispenser dispenser, string name, string itemType,
                                               string equipmentId = null, GameObject brokenMarker = null)
        {
            dispenser.Configure(name, () => ItemFactory.Create(itemType, dispenser.transform.position, Quaternion.identity),
                                equipmentId, brokenMarker);
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

            if (Session.State.Current == MatchState.Ready && (AutoStart || _autoStartNext))
            {
                _readyTimer += Time.deltaTime;
                if (_readyTimer >= _readySeconds)
                {
                    StartFlight();
                }
            }

            Session.Tick(Time.deltaTime);
        }

        /// <summary>Botao "Jogar" do menu principal.</summary>
        public void StartFlight()
        {
            _readyTimer = 0f;
            _autoStartNext = false;
            Session.StartBoarding(out _);
        }

        /// <summary>
        /// Pedido vindo da UI; executado no proximo Update (fora do OnGUI).
        /// <paramref name="toMenu"/> = volta ao menu principal; senao comeca outra viagem direto.
        /// </summary>
        public void RequestRestart(bool toMenu = false)
        {
            _restartRequested = true;
            _restartToMenu = toMenu;
        }

        private void Restart()
        {
            if (Session.State.IsPlayable)
            {
                // Reiniciar no meio do voo encerra a viagem atual como derrota (regra de transicao).
                Session.Finish(false, "Viagem interrompida pelo jogador");
            }

            if (_restartToMenu)
            {
                Session.State.TryTransition(MatchState.ReturningToMenu, out _);
            }

            _autoStartNext = !_restartToMenu;

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

            _world.ClearAll();
            Destroy(_dynamicRoot.gameObject);
            _dynamicRoot = new GameObject("Dinamicos").transform;
            _dynamicRoot.SetParent(transform, false);
            _player.Motor.Teleport(CabinLayout.PlayerSpawn, Quaternion.Euler(0f, 180f, 0f));
            Session.CompleteInitStep(StepPlayer);
            SpawnDynamicContent();
            Session.CompleteInitStep(StepPassengers);
        }

        /// <summary>Volta ao menu principal (a viagem atual termina).</summary>
        public void QuitToMenu() => RequestRestart(toMenu: true);

        /// <summary>Troca uniforme/acessorio (so no menu). Recria o visual e a animacao.</summary>
        public void ApplyOutfit()
        {
            var go = _player.gameObject;
            foreach (var old in go.GetComponents<MonoBehaviour>())
            {
                if (old is ICharacterAnimation)
                {
                    Destroy(old);
                }
            }

            if (_playerVisual != null)
            {
                Destroy(_playerVisual.gameObject);
            }

            _playerVisual = OutfitApplier.Dress(go.transform, Save.Data.EquippedUniform, Save.Data.EquippedAccessory);
            _player.SetAnimation(CharacterAnimation.Attach(go, _playerVisual));
        }

        /// <summary>Aplica configuracoes alteradas (camera e audio) e salva.</summary>
        public void ApplySettings()
        {
            var settings = Save.Data.Settings;
            _cameraRig.Sensitivity = settings.MouseSensitivity;
            _cameraRig.InvertY = settings.InvertY;
            _audio?.ApplySettings(settings);
            Save.SaveSettings();
        }

        public void QuitGame()
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
