using System;
using System.Collections.Generic;
using SkyCrew.Domain.Scoring;

namespace SkyCrew.Domain.Tasks
{
    public enum TaskState
    {
        Available,
        Active,
        Completed,
        Failed,
        Cancelled,
    }

    public enum TaskPriority
    {
        Low,
        Normal,
        High,
        Urgent,
    }

    /// <summary>Definicao imutavel de uma tarefa (dados). O estado fica em <see cref="TaskRuntime"/>.</summary>
    public sealed class TaskDefinition
    {
        private readonly ObjectiveDefinition[] _objectives;
        private readonly string[] _prerequisites;

        public TaskDefinition(string id, string displayName, string description,
                              ObjectiveDefinition[] objectives,
                              TaskPriority priority = TaskPriority.Normal,
                              bool isMandatory = false,
                              float timeLimitSeconds = 0f,
                              int reward = 100,
                              int penalty = 0,
                              string[] prerequisites = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Tarefa sem identificador", nameof(id));
            }

            if (objectives == null || objectives.Length == 0)
            {
                throw new ArgumentException($"Tarefa '{id}' sem objetivos", nameof(objectives));
            }

            Id = id;
            DisplayName = displayName;
            Description = description;
            _objectives = (ObjectiveDefinition[])objectives.Clone();
            Priority = priority;
            IsMandatory = isMandatory;
            TimeLimitSeconds = Math.Max(0f, timeLimitSeconds);
            Reward = Math.Max(0, reward);
            Penalty = Math.Max(0, penalty);
            _prerequisites = prerequisites == null ? Array.Empty<string>() : (string[])prerequisites.Clone();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public TaskPriority Priority { get; }
        public bool IsMandatory { get; }

        /// <summary>0 = sem prazo.</summary>
        public float TimeLimitSeconds { get; }

        public int Reward { get; }
        public int Penalty { get; }
        public IReadOnlyList<ObjectiveDefinition> Objectives => _objectives;
        public IReadOnlyList<string> Prerequisites => _prerequisites;
    }

    /// <summary>Estado de execucao de uma tarefa durante a partida.</summary>
    public sealed class TaskRuntime
    {
        private readonly ITaskObjective[] _objectives;

        internal TaskRuntime(TaskDefinition definition)
        {
            Definition = definition;
            State = TaskState.Available;
            _objectives = new ITaskObjective[definition.Objectives.Count];
            for (int i = 0; i < _objectives.Length; i++)
            {
                _objectives[i] = definition.Objectives[i].CreateRuntime();
            }
        }

        public TaskDefinition Definition { get; }
        public string Id => Definition.Id;
        public TaskState State { get; internal set; }
        public float Elapsed { get; internal set; }
        public IReadOnlyList<ITaskObjective> Objectives => _objectives;
        public string EndReason { get; internal set; }

        public float TimeLeft => Definition.TimeLimitSeconds > 0f
            ? Math.Max(0f, Definition.TimeLimitSeconds - Elapsed)
            : float.PositiveInfinity;

        public bool AllObjectivesComplete
        {
            get
            {
                foreach (var o in _objectives)
                {
                    if (!o.IsComplete)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public float Progress
        {
            get
            {
                float sum = 0f;
                foreach (var o in _objectives)
                {
                    sum += o.Progress;
                }

                return sum / _objectives.Length;
            }
        }

        public bool IsResolved => State == TaskState.Completed || State == TaskState.Failed || State == TaskState.Cancelled;
    }

    /// <summary>
    /// Ciclo de vida das tarefas: registro, ativacao (com pre-requisitos), progresso por sinais,
    /// prazo, conclusao/falha/cancelamento e recompensa aplicada uma unica vez.
    /// </summary>
    public sealed class TaskManager
    {
        private readonly Dictionary<string, TaskRuntime> _tasks = new Dictionary<string, TaskRuntime>(StringComparer.Ordinal);
        private readonly List<TaskRuntime> _order = new List<TaskRuntime>();
        private readonly List<TaskRuntime> _scratch = new List<TaskRuntime>();
        private readonly IScoreSink _score;
        private readonly Func<bool> _canActivate;

        /// <param name="score">Destino de recompensas/penalidades.</param>
        /// <param name="canActivate">Regra externa (ex.: estado da partida permite tarefas).</param>
        public TaskManager(IScoreSink score, Func<bool> canActivate = null)
        {
            _score = score ?? throw new ArgumentNullException(nameof(score));
            _canActivate = canActivate ?? (() => true);
        }

        /// <summary>(tarefa, estado anterior)</summary>
        public event Action<TaskRuntime, TaskState> StateChanged;

        public event Action<TaskRuntime> ProgressChanged;

        public IReadOnlyList<TaskRuntime> All => _order;

        public void Register(TaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (_tasks.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"Tarefa '{definition.Id}' ja registrada");
            }

            var runtime = new TaskRuntime(definition);
            _tasks.Add(definition.Id, runtime);
            _order.Add(runtime);
        }

        public bool IsRegistered(string id) => _tasks.ContainsKey(id);

        public TaskRuntime Get(string id)
        {
            _tasks.TryGetValue(id, out var t);
            return t;
        }

        public bool TryActivate(string id, out string reason)
        {
            if (!_tasks.TryGetValue(id, out var task))
            {
                reason = $"Tarefa '{id}' nao existe";
                return false;
            }

            if (task.State != TaskState.Available)
            {
                reason = $"Tarefa '{id}' esta {task.State}";
                return false;
            }

            if (!_canActivate())
            {
                reason = "O estado atual da partida nao permite ativar tarefas";
                return false;
            }

            foreach (string pre in task.Definition.Prerequisites)
            {
                var p = Get(pre);
                if (p == null || p.State != TaskState.Completed)
                {
                    reason = $"Pre-requisito '{pre}' ainda nao concluido";
                    return false;
                }
            }

            task.Elapsed = 0f;
            SetState(task, TaskState.Active, null);
            reason = null;

            // Um objetivo pode ja estar satisfeito? Nao: objetivos so contam sinais apos a ativacao.
            return true;
        }

        /// <summary>Entrega um sinal do mundo para todas as tarefas ativas.</summary>
        public void HandleSignal(in GameplaySignal signal)
        {
            _scratch.Clear();
            foreach (var t in _order)
            {
                if (t.State == TaskState.Active)
                {
                    _scratch.Add(t);
                }
            }

            foreach (var task in _scratch)
            {
                bool changed = false;
                foreach (var objective in task.Objectives)
                {
                    if (objective.Handle(signal))
                    {
                        changed = true;
                    }
                }

                if (!changed)
                {
                    continue;
                }

                ProgressChanged?.Invoke(task);
                if (task.AllObjectivesComplete)
                {
                    Complete(task);
                }
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _scratch.Clear();
            foreach (var t in _order)
            {
                if (t.State == TaskState.Active)
                {
                    _scratch.Add(t);
                }
            }

            foreach (var task in _scratch)
            {
                task.Elapsed += deltaSeconds;
                if (task.Definition.TimeLimitSeconds > 0f && task.Elapsed >= task.Definition.TimeLimitSeconds)
                {
                    Fail(task.Id, "Prazo esgotado");
                }
            }
        }

        public bool Fail(string id, string reason)
        {
            var task = Get(id);
            if (task == null || task.State != TaskState.Active)
            {
                return false;
            }

            SetState(task, TaskState.Failed, reason);
            if (task.Definition.Penalty > 0)
            {
                _score.TryApply("tarefa_falhou:" + id, -task.Definition.Penalty, $"Falhou: {task.Definition.DisplayName}");
            }

            CancelDependents(id);
            return true;
        }

        /// <summary>Cancela a tarefa (sem recompensa) e, em cascata, as que dependem dela.</summary>
        public bool Cancel(string id, string reason)
        {
            var task = Get(id);
            if (task == null || task.IsResolved)
            {
                return false;
            }

            SetState(task, TaskState.Cancelled, reason);
            CancelDependents(id);
            return true;
        }

        private void CancelDependents(string id)
        {
            foreach (var other in _order.ToArray())
            {
                if (other.IsResolved)
                {
                    continue;
                }

                foreach (string pre in other.Definition.Prerequisites)
                {
                    if (pre == id)
                    {
                        Cancel(other.Id, $"Dependencia '{id}' nao foi concluida");
                        break;
                    }
                }
            }
        }

        /// <summary>Concluir uma tarefa com prazo usando no maximo esta fracao do tempo rende bonus.</summary>
        public const float QuickResponseFraction = 0.5f;

        /// <summary>Bonus de resposta rapida = recompensa / 4.</summary>
        public const int QuickResponseDivisor = 4;

        private void Complete(TaskRuntime task)
        {
            SetState(task, TaskState.Completed, null);
            var def = task.Definition;
            _score.TryApply("tarefa:" + task.Id, def.Reward, $"Concluida: {def.DisplayName}");
            if (def.TimeLimitSeconds > 0f && task.Elapsed <= def.TimeLimitSeconds * QuickResponseFraction && def.Reward > 0)
            {
                _score.TryApply("tarefa_rapida:" + task.Id, def.Reward / QuickResponseDivisor,
                                $"Atendimento rapido: {def.DisplayName}");
            }
        }

        private void SetState(TaskRuntime task, TaskState state, string reason)
        {
            var previous = task.State;
            task.State = state;
            task.EndReason = reason;
            StateChanged?.Invoke(task, previous);
        }
    }
}
