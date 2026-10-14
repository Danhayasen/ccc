using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkyCrew.Player
{
    /// <summary>Comandos do jogador, independentes da origem (teclado, controle, rede, testes).</summary>
    public interface IPlayerCommandSource
    {
        Vector2 Move { get; }
        Vector2 Look { get; }
        bool Sprint { get; }
        bool JumpPressed { get; }
        bool InteractPressed { get; }
        bool TasksHeld { get; }
        bool PausePressed { get; }
    }

    /// <summary>
    /// Le o Input System com acoes criadas em codigo (WASD, mouse, Shift, Espaco, E, Tab, Esc
    /// e controle). Rebinding futuro: <c>ApplyBindingOverride</c> nas acoes expostas.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour, IPlayerCommandSource
    {
        [SerializeField] private float _mouseScale = 0.08f;
        [SerializeField] private float _stickScale = 2.2f;

        private InputAction _move, _lookMouse, _lookStick, _sprint, _jump, _interact, _tasks, _pause;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool TasksHeld { get; private set; }
        public bool PausePressed { get; private set; }

        /// <summary>Quando false (menus abertos), so Pause continua sendo lido.</summary>
        public bool GameplayEnabled { get; set; } = true;

        public InputAction InteractAction => _interact;

        private void Awake()
        {
            _move = new InputAction("Mover", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                 .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                 .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _lookMouse = new InputAction("OlharMouse", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("OlharControle", InputActionType.Value, "<Gamepad>/rightStick");
            _sprint = new InputAction("Correr", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");
            _jump = new InputAction("Pular", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");
            _interact = new InputAction("Interagir", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
            _tasks = new InputAction("Tarefas", InputActionType.Button, "<Keyboard>/tab");
            _tasks.AddBinding("<Gamepad>/select");
            _pause = new InputAction("Pausar", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");
        }

        private InputAction[] All => new[] { _move, _lookMouse, _lookStick, _sprint, _jump, _interact, _tasks, _pause };

        private void OnEnable()
        {
            foreach (var a in All)
            {
                a.Enable();
            }
        }

        private void OnDisable()
        {
            foreach (var a in All)
            {
                a.Disable();
            }
        }

        private void OnDestroy()
        {
            foreach (var a in All)
            {
                a.Dispose();
            }
        }

        private void Update()
        {
            PausePressed = _pause.WasPressedThisFrame();
            if (!GameplayEnabled)
            {
                Move = Vector2.zero;
                Look = Vector2.zero;
                Sprint = JumpPressed = InteractPressed = TasksHeld = false;
                return;
            }

            Move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            Look = _lookMouse.ReadValue<Vector2>() * _mouseScale
                   + _lookStick.ReadValue<Vector2>() * (_stickScale * Time.unscaledDeltaTime * 60f);
            Sprint = _sprint.IsPressed();
            JumpPressed = _jump.WasPressedThisFrame();
            InteractPressed = _interact.WasPressedThisFrame();
            TasksHeld = _tasks.IsPressed();
        }

        public string DescribeBindings() => "WASD mover · Mouse câmera · Shift correr · Espaço pular · E interagir · Tab tarefas · Esc pausar";
    }
}
