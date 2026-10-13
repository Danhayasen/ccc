using System.Linq;
using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Tasks;
using UnityEditor;
using UnityEngine;

namespace SkyCrew.EditorTools
{
    /// <summary>
    /// Painel de diagnostico (Parte 19): estado da partida, tarefas, eventos e passageiros ao vivo,
    /// com historico de estados e botoes para forcar eventos. So le o dominio; os botoes de teste
    /// passam pelas mesmas regras de elegibilidade (exceto o intervalo global).
    /// Menu: SkyCrew > Diagnóstico.
    /// </summary>
    public sealed class SkyCrewDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _lastMessage;
        private string _expandedPassenger;

        [MenuItem("SkyCrew/Diagnóstico", priority = 20)]
        public static void Open() => GetWindow<SkyCrewDiagnosticsWindow>("SkyCrew Diagnóstico");

        private void OnInspectorUpdate() => Repaint(); // ~10x por segundo durante o Play

        private void OnGUI()
        {
            var session = GameBootstrap.Session;
            if (!Application.isPlaying || session == null)
            {
                EditorGUILayout.HelpBox("Entre no Play com a cena Prototype para ver o diagnóstico.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawMatch(session);
            DrawEvents(session);
            DrawTasks(session);
            DrawPassengers(session);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMatch(MatchSession s)
        {
            EditorGUILayout.LabelField("Partida", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Estado", s.State.Current.ToString());
            EditorGUILayout.LabelField("Tempo de voo", $"{s.FlightTime:0.0}s  (restam {s.FlightTimeLeft:0}s)");
            EditorGUILayout.LabelField("Satisfação média", $"{s.AverageSatisfaction:P0}");
            EditorGUILayout.LabelField("Pontos", $"{s.Score.Total}  (+{s.Score.Rewards} / -{s.Score.Penalties})");
            EditorGUILayout.LabelField("Pedidos abertos", $"{s.OpenRequests}/{s.Config.MaxOpenRequests}   ajudantes ativos: {s.ActiveHelpers}");
            EditorGUILayout.LabelField("Cinto / turbulência", $"{(s.SeatbeltSignOn ? "ACESO" : "apagado")} / {s.TurbulenceIntensity:0.00}");
            EditorGUILayout.LabelField("Geladeira", s.IsEquipmentAvailable(PrototypeScenario.EquipmentFridge) ? "ok" : "QUEBRADA");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Pular embarque") && s.State.Current == MatchState.Boarding)
                {
                    s.Tick(s.BoardingTimeLeft + 0.01f);
                }

                if (GUILayout.Button("Pousar agora (avaliar vitória)") && s.State.Current == MatchState.InFlight)
                {
                    s.Tick(s.FlightTimeLeft + 0.01f);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawEvents(MatchSession s)
        {
            EditorGUILayout.LabelField("Eventos", EditorStyles.boldLabel);
            foreach (var e in s.Events.Active)
            {
                EditorGUILayout.LabelField($"  ATIVO {e.InstanceId}", $"{e.Remaining:0.0}s restantes, intensidade {e.Intensity:0.00}");
            }

            var ctx = new EventContext(s.State.Current, s.FlightTime, s.Config.Difficulty);
            foreach (var def in s.Events.Definitions)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool ok = s.Events.IsEligible(def, ctx, true, out string reason);
                    EditorGUILayout.LabelField(def.DisplayName, ok ? "elegível" : reason);
                    if (GUILayout.Button("Forçar", GUILayout.Width(70)))
                    {
                        _lastMessage = s.Events.TryStart(def.Id, ctx, out string why, ignoreGlobalInterval: true)
                            ? $"{def.Id} iniciado" : $"{def.Id} recusado: {why}";
                    }
                }
            }

            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.HelpBox(_lastMessage, MessageType.None);
            }

            EditorGUILayout.Space();
        }

        private void DrawTasks(MatchSession s)
        {
            EditorGUILayout.LabelField("Tarefas", EditorStyles.boldLabel);
            foreach (var t in s.Tasks.All.Where(t => t.State != TaskState.Available))
            {
                string limit = t.Definition.TimeLimitSeconds > 0f && t.State == TaskState.Active ? $" {t.TimeLeft:0}s" : "";
                string mandatory = t.Definition.IsMandatory ? " [obrigatória]" : "";
                EditorGUILayout.LabelField($"  {t.Id}{mandatory}", $"{t.State} {t.Progress:P0}{limit}");
            }

            EditorGUILayout.Space();
        }

        private void DrawPassengers(MatchSession s)
        {
            EditorGUILayout.LabelField("Passageiros", EditorStyles.boldLabel);
            foreach (var p in s.Passengers)
            {
                var n = p.Needs;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string request = p.PendingService != ServiceKind.None ? $" pede {p.PendingService}" : "";
                    if (GUILayout.Button($"{p.Id} ({p.Profile.DisplayName}): {p.State}{request}", EditorStyles.foldout))
                    {
                        _expandedPassenger = _expandedPassenger == p.Id ? null : p.Id;
                    }
                }

                Bar("  satisfação", n.Satisfaction);
                Bar("  medo", n.Fear);
                if (_expandedPassenger != p.Id)
                {
                    continue;
                }

                Bar("  paciência", n.Patience);
                Bar("  conforto", n.Comfort);
                Bar("  sede", n.Thirst);
                Bar("  fome", n.Hunger);
                Bar("  frio", n.Cold);
                Bar("  banheiro", n.Lavatory);
                EditorGUILayout.LabelField("  histórico (últimas transições):");
                foreach (string line in p.History.Reverse().Take(8))
                {
                    EditorGUILayout.LabelField("    " + line);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Assustar")) p.Scare(0.8f);
                    if (GUILayout.Button("Dar sede")) n.DebugSet(thirst: 1f);
                    if (GUILayout.Button("Dar frio")) n.DebugSet(cold: 1f);
                }
            }
        }

        private static void Bar(string label, float value)
        {
            var rect = EditorGUILayout.GetControlRect(false, 14f);
            var labelRect = new Rect(rect.x, rect.y, 110f, rect.height);
            var barRect = new Rect(rect.x + 112f, rect.y + 2f, rect.width - 112f, rect.height - 4f);
            EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
            EditorGUI.ProgressBar(barRect, Mathf.Clamp01(value), $"{value:0.00}");
        }
    }
}
