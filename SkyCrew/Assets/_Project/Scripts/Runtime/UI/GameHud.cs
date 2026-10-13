using System.Collections.Generic;
using SkyCrew.Core;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Scoring;
using SkyCrew.Domain.Tasks;
using SkyCrew.PassengerAI;
using SkyCrew.Player;
using UnityEngine;

namespace SkyCrew.UI
{
    /// <summary>
    /// Interface PROVISORIA (IMGUI) da Fase 1: tarefas, fase/tempo, satisfacao, aviso de cinto,
    /// acao contextual, baloes de pedido, avisos, pausa e resultados. So LE o estado da partida;
    /// mudancas passam por GameBootstrap (reiniciar, sair). Sera trocada por UI Toolkit na Fase 6.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private struct Toast
        {
            public string Text;
            public Color Color;
            public float Until;
        }

        private GameBootstrap _game;
        private PlayerController _player;
        private PlayerInputReader _input;
        private Camera _camera;
        private readonly List<Toast> _toasts = new List<Toast>();
        private TaskManager _subscribedTasks;
        private bool _paused;
        private float _scale = 1f;

        private GUIStyle _panel, _title, _text, _small, _prompt, _bubble, _button, _big;
        private Texture2D _white;

        public bool IsPaused => _paused;

        public void Bind(GameBootstrap game, PlayerController player, PlayerInputReader input, Camera cam)
        {
            _game = game;
            _player = player;
            _input = input;
            _camera = cam;
        }

        private void Update()
        {
            var session = GameBootstrap.Session;
            if (session == null)
            {
                return;
            }

            if (_subscribedTasks != session.Tasks)
            {
                if (_subscribedTasks != null)
                {
                    _subscribedTasks.StateChanged -= OnTaskStateChanged;
                }

                _subscribedTasks = session.Tasks;
                _subscribedTasks.StateChanged += OnTaskStateChanged;
            }

            bool ended = session.State.Current == MatchState.Results || session.State.Current == MatchState.Failed;
            if (_input != null && _input.PausePressed && !ended)
            {
                SetPaused(!_paused);
            }

            bool menuOpen = _paused || ended;
            if (_input != null)
            {
                _input.GameplayEnabled = !menuOpen;
            }

            Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = menuOpen;
            _toasts.RemoveAll(t => Time.unscaledTime > t.Until);
        }

        /// <summary>Solo: pausa congela a simulacao. No multiplayer a pausa sera so local.</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
        }

        private void OnTaskStateChanged(TaskRuntime task, TaskState previous)
        {
            switch (task.State)
            {
                case TaskState.Active:
                    AddToast("Nova tarefa: " + task.Definition.DisplayName,
                             task.Definition.Priority == TaskPriority.Urgent ? MaterialLibrary.Vermelho : MaterialLibrary.AzulClaro);
                    break;
                case TaskState.Completed:
                    AddToast($"+{task.Definition.Reward}  {task.Definition.DisplayName}", MaterialLibrary.Verde);
                    break;
                case TaskState.Failed:
                    AddToast($"Falhou: {task.Definition.DisplayName} ({task.EndReason})", MaterialLibrary.Vermelho);
                    break;
            }
        }

        private void AddToast(string text, Color color)
        {
            _toasts.Add(new Toast { Text = text, Color = color, Until = Time.unscaledTime + 3.5f });
        }

        // --- Desenho ---------------------------------------------------------------

        private void EnsureStyles()
        {
            _scale = Mathf.Max(0.75f, Screen.height / 1080f);
            if (_white == null)
            {
                _white = Texture2D.whiteTexture;
            }

            int Size(int px) => Mathf.RoundToInt(px * _scale);
            _panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(Size(14), Size(14), Size(10), Size(10)) };
            _title = new GUIStyle(GUI.skin.label) { fontSize = Size(24), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _text = new GUIStyle(GUI.skin.label) { fontSize = Size(18), wordWrap = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(_text) { fontSize = Size(14), normal = { textColor = new Color(0.85f, 0.88f, 0.95f) } };
            _prompt = new GUIStyle(_text) { fontSize = Size(22), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _bubble = new GUIStyle(_text) { fontSize = Size(16), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = MaterialLibrary.AzulEscuro } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = Size(22), fixedHeight = Size(54) };
            _big = new GUIStyle(_title) { fontSize = Size(44), alignment = TextAnchor.MiddleCenter };
        }

        private void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private void Bar(Rect r, float value, Color color)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), color);
        }

        private void OnGUI()
        {
            var session = GameBootstrap.Session;
            if (session == null)
            {
                return;
            }

            EnsureStyles();
            float s = _scale;

            DrawBubbles(session);
            DrawTasks(session, s);
            DrawStatus(session, s);
            DrawPrompt(s);
            DrawToasts(s);

            switch (session.State.Current)
            {
                case MatchState.Initializing:
                    DrawCenterMessage(session.PendingInitSteps.Count > 0
                        ? "Preparando a aeronave..." : "Falha ao preparar a aeronave (veja o Console)");
                    break;
                case MatchState.Ready:
                    DrawCenterMessage("Prepare-se para o embarque!");
                    if (_game.Save.LoadStatus == SkyCrew.Domain.Persistence.SaveLoadStatus.Corrupted)
                    {
                        GUI.Label(new Rect(0, Screen.height * 0.35f + 90f * s, Screen.width, 30f * s),
                                  "O progresso salvo estava danificado: começando do zero (cópia guardada).",
                                  new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
                    }

                    break;
                case MatchState.Results:
                case MatchState.Failed:
                    if (session.Results != null)
                    {
                        DrawResults(session.Results, s);
                    }

                    break;
            }

            if (_paused)
            {
                DrawPause(s);
            }
        }

        private void DrawTasks(MatchSession session, float s)
        {
            bool expanded = _input != null && _input.TasksHeld;
            float w = (expanded ? 460f : 360f) * s;
            float x = 20f * s, y = 20f * s;
            float lineH = 26f * s;

            var visible = new List<TaskRuntime>();
            foreach (var t in session.Tasks.All)
            {
                if (t.State != TaskState.Available && t.State != TaskState.Cancelled)
                {
                    visible.Add(t);
                }
            }

            float h = 50f * s + visible.Count * (expanded ? 3.2f : 2.1f) * lineH;
            Fill(new Rect(x, y, w, h), new Color(0.06f, 0.1f, 0.2f, 0.78f));
            GUI.Label(new Rect(x + 14 * s, y + 8 * s, w, 34 * s), expanded ? "Tarefas (Tab)" : "Tarefas  [Tab]", _title);
            float cy = y + 48f * s;
            foreach (var t in visible)
            {
                Color c = t.State == TaskState.Completed ? MaterialLibrary.Verde
                    : t.State == TaskState.Failed ? MaterialLibrary.Vermelho
                    : t.Definition.Priority == TaskPriority.Urgent ? new Color(1f, 0.55f, 0.45f)
                    : t.Definition.Priority == TaskPriority.High ? MaterialLibrary.Amarelo : Color.white;
                string mark = t.State == TaskState.Completed ? "✔ " : t.State == TaskState.Failed ? "✖ " : "• ";
                string timer = t.State == TaskState.Active && t.Definition.TimeLimitSeconds > 0f ? $"  {Mathf.CeilToInt(t.TimeLeft)}s" : "";
                string mandatory = t.Definition.IsMandatory ? "" : "  (extra)";
                var style = new GUIStyle(_text) { normal = { textColor = c } };
                GUI.Label(new Rect(x + 14 * s, cy, w - 28 * s, lineH), mark + t.Definition.DisplayName + mandatory + timer, style);
                cy += lineH;
                Bar(new Rect(x + 30 * s, cy + 4 * s, w - 60 * s, 8 * s), t.State == TaskState.Completed ? 1f : t.Progress, c);
                cy += lineH * 1.1f - 4 * s;
                if (expanded)
                {
                    GUI.Label(new Rect(x + 30 * s, cy - 6 * s, w - 44 * s, lineH * 1.2f), t.Definition.Description, _small);
                    cy += lineH * 1.1f;
                }
            }
        }

        private void DrawStatus(MatchSession session, float s)
        {
            float w = 330f * s, x = Screen.width - w - 20f * s, y = 20f * s;
            Fill(new Rect(x, y, w, 132f * s), new Color(0.06f, 0.1f, 0.2f, 0.78f));
            string phase;
            switch (session.State.Current)
            {
                case MatchState.Boarding:
                    phase = $"Embarque  {Clock(session.BoardingTimeLeft)}";
                    break;
                case MatchState.InFlight:
                    phase = $"Em voo  {Clock(session.FlightTimeLeft)}";
                    break;
                default:
                    phase = session.State.Current.ToString();
                    break;
            }

            GUI.Label(new Rect(x + 14 * s, y + 8 * s, w, 34 * s), phase, _title);
            GUI.Label(new Rect(x + 14 * s, y + 46 * s, w, 26 * s), "Satisfação da cabine", _small);
            float sat = session.AverageSatisfaction;
            Bar(new Rect(x + 14 * s, y + 72 * s, w - 28 * s, 14 * s), sat,
                Color.Lerp(MaterialLibrary.Vermelho, MaterialLibrary.Verde, sat));
            GUI.Label(new Rect(x + 14 * s, y + 92 * s, w, 30 * s), $"Pontos: {session.Score.Total}", _text);

            if (!session.IsEquipmentAvailable(SkyCrew.Domain.Content.PrototypeScenario.EquipmentFridge))
            {
                Fill(new Rect(x, y + 186f * s, w, 34f * s), new Color(0.6f, 0.1f, 0.1f, 0.85f));
                GUI.Label(new Rect(x, y + 186f * s, w, 34f * s), "Geladeira quebrada!", new GUIStyle(_prompt) { fontSize = Mathf.RoundToInt(17 * s) });
            }

            if (session.SeatbeltSignOn)
            {
                bool blink = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f;
                Fill(new Rect(x, y + 140f * s, w, 40f * s), blink ? MaterialLibrary.Amarelo : new Color(0.5f, 0.4f, 0.1f));
                var st = new GUIStyle(_prompt) { normal = { textColor = MaterialLibrary.AzulEscuro }, fontSize = Mathf.RoundToInt(18 * s) };
                GUI.Label(new Rect(x, y + 140f * s, w, 40f * s), "APERTEM OS CINTOS — Turbulência", st);
            }
        }

        private static string Clock(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        private void DrawPrompt(float s)
        {
            if (_player == null || _paused)
            {
                return;
            }

            var interactor = _player.Interactor;
            float w = 640f * s, h = 44f * s;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height - 150f * s, w, h);
            if (interactor.Current != null)
            {
                bool ok = interactor.CurrentCheck.Allowed;
                Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                string text = ok
                    ? "[E]  " + interactor.Current.GetPrompt(interactor)
                    : interactor.Current.GetPrompt(interactor) + "  —  " + interactor.CurrentCheck.Reason;
                var st = new GUIStyle(_prompt) { normal = { textColor = ok ? Color.white : new Color(0.75f, 0.75f, 0.75f) } };
                GUI.Label(rect, text, st);
            }

            var held = interactor.Hands != null ? interactor.Hands.Held : null;
            if (held != null)
            {
                GUI.Label(new Rect(rect.x, rect.y + h + 6f * s, w, 30f * s),
                          $"Segurando: {held.DisplayName}   ([E] sem alvo = soltar)", new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
            }

            if (!string.IsNullOrEmpty(interactor.LastFeedback) && Time.time - interactor.LastFeedbackTime < 2f)
            {
                GUI.Label(new Rect(rect.x, rect.y - 40f * s, w, 34f * s), interactor.LastFeedback,
                          new GUIStyle(_prompt) { normal = { textColor = MaterialLibrary.Amarelo } });
            }

            GUI.Label(new Rect(20f * s, Screen.height - 40f * s, 900f * s, 30f * s),
                      "WASD mover · Mouse câmera · Shift correr · Espaço pular · E interagir · Tab tarefas · Esc pausar", _small);
        }

        private void DrawToasts(float s)
        {
            float y = 210f * s;
            foreach (var t in _toasts)
            {
                var rect = new Rect(Screen.width - 520f * s, y, 500f * s, 36f * s);
                Fill(rect, new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(rect.x, rect.y, 6f * s, rect.height), t.Color);
                GUI.Label(new Rect(rect.x + 14f * s, rect.y, rect.width, rect.height), t.Text, _text);
                y += 42f * s;
            }
        }

        private void DrawBubbles(MatchSession session)
        {
            if (_camera == null)
            {
                return;
            }

            foreach (var p in _game.Passengers)
            {
                if (p == null || p.Brain == null)
                {
                    continue;
                }

                string text = null;
                Color bg = Color.white;
                if (p.IsReacting)
                {
                    text = "!!";
                    bg = new Color(1f, 0.6f, 0.55f);
                }
                else if (p.ShownRequest != ServiceKind.None)
                {
                    text = p.ShownRequest == ServiceKind.Water ? "Água!"
                         : p.ShownRequest == ServiceKind.Snack ? "Lanche!" : "Cobertor!";
                    bg = new Color(0.8f, 0.92f, 1f);
                }

                if (text == null)
                {
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(p.transform.position + Vector3.up * 1.9f);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float w = 80f * _scale, h = 34f * _scale;
                var rect = new Rect(screen.x - w / 2f, Screen.height - screen.y - h, w, h);
                Fill(rect, bg);
                GUI.Label(rect, text, _bubble);
            }
        }

        private void DrawCenterMessage(string message)
        {
            float w = 800f * _scale, h = 80f * _scale;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.35f, w, h);
            Fill(rect, new Color(0.06f, 0.1f, 0.2f, 0.85f));
            GUI.Label(rect, message, new GUIStyle(_big) { fontSize = Mathf.RoundToInt(30 * _scale) });
        }

        private void DrawPause(float s)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            float w = 420f * s;
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2f, Screen.height * 0.3f, w, 400f * s));
            GUILayout.Label("Pausado", _big);
            GUILayout.Space(20f * s);
            if (GUILayout.Button("Continuar", _button)) SetPaused(false);
            if (GUILayout.Button("Reiniciar viagem", _button)) { SetPaused(false); _game.RequestRestart(); }
            if (GUILayout.Button("Sair", _button)) { SetPaused(false); _game.QuitToMenu(); }
            GUILayout.EndArea();
        }

        private void DrawResults(MatchResults r, float s)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.05f, 0.12f, 0.8f));
            float w = 640f * s;
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2f, Screen.height * 0.12f, w, Screen.height * 0.8f));
            GUILayout.Label(r.Victory ? "Viagem concluída!" : "Voo complicado...", _big);
            GUILayout.Label(r.Reason, new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter });
            string stars = new string('★', r.Score.Stars) + new string('☆', 3 - r.Score.Stars);
            GUILayout.Label(stars, new GUIStyle(_big) { normal = { textColor = MaterialLibrary.Amarelo } });
            GUILayout.Space(10f * s);
            foreach (string line in r.Lines)
            {
                GUILayout.Label(line, _text);
            }

            ScoreBreakdown b = r.Score;
            GUILayout.Label($"Satisfação média: {r.AverageSatisfaction:P0}  (+{b.SatisfactionBonus})", _text);
            GUILayout.Label($"Tarefas (com bônus de resposta rápida): +{b.TaskRewards}   Penalidades: -{b.TaskPenalties}", _text);
            GUILayout.Label($"Tarefas concluídas: {r.TasksCompleted}   Falhas: {r.TasksFailed}", _text);
            GUILayout.Label($"Pontuação final: {b.Total}", _title);
            DrawProgress(s);
            GUILayout.Space(16f * s);
            if (GUILayout.Button("Jogar de novo", _button)) _game.RequestRestart();
            if (GUILayout.Button("Sair", _button)) _game.QuitToMenu();
            GUILayout.EndArea();
        }

        private void DrawProgress(float s)
        {
            var save = _game != null ? _game.Save : null;
            if (save == null)
            {
                return;
            }

            var stats = save.Data.Stats;
            var report = save.LastReport;
            string best = report != null && report.NewBestScore ? "  — novo recorde!" : "";
            GUILayout.Label($"Estrelas no total: {stats.TotalStars}   Voos: {stats.FlightsWon}/{stats.FlightsPlayed}   Recorde: {stats.BestScore}{best}", _small);
            if (report != null)
            {
                foreach (var unlock in report.NewUnlocks)
                {
                    GUILayout.Label("Desbloqueado: " + unlock.DisplayName, new GUIStyle(_text) { normal = { textColor = MaterialLibrary.Amarelo } });
                }
            }

            if (!string.IsNullOrEmpty(save.LastSaveError))
            {
                GUILayout.Label("Não foi possível salvar o progresso: " + save.LastSaveError, _small);
            }
        }

        private void OnDestroy()
        {
            if (_subscribedTasks != null)
            {
                _subscribedTasks.StateChanged -= OnTaskStateChanged;
            }

            Time.timeScale = 1f;
        }
    }
}
