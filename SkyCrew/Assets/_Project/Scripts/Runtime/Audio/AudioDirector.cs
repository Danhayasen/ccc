using System.Collections.Generic;
using SkyCrew.Core;
using SkyCrew.Domain.Audio;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Tasks;
using SkyCrew.Interaction;
using SkyCrew.Physics;
using SkyCrew.Player;
using UnityEngine;

namespace SkyCrew.Audio
{
    /// <summary>
    /// Feedback sonoro: escuta eventos do jogo e toca sons pelo AudioRouter (dominio), que
    /// aplica volume por canal, limite de repeticao e de instancias. Loops: motor (ambiente),
    /// turbulencia (ambiente) e musica. A ausencia de um som nunca impede o jogo.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private const int PoolSize = 16;

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private AudioRouter _router;
        private AudioSource _engine, _rumble, _music;
        private MatchSession _session;
        private TaskManager _tasks;
        private EventScheduler _events;
        private bool _seatbelt;
        private int _next;

        public AudioRouter Router => _router;

        public void Bind(MatchSession session, PrototypeScenario scenario, PlayerMotor player, Interactor interactor,
                         SkyCrew.Domain.Persistence.GameSettings settings)
        {
            _session = session;
            _router = new AudioRouter(new DeterministicRandom(session.Config.Seed ^ 0xA5A5UL), AudioCatalog.Default());
            for (int i = 0; i < PoolSize; i++)
            {
                var src = new GameObject("Som_" + i).AddComponent<AudioSource>();
                src.transform.SetParent(transform, false);
                src.playOnAwake = false;
                src.minDistance = 1.5f;
                src.maxDistance = 18f;
                src.rolloffMode = AudioRolloffMode.Linear;
                _pool.Add(src);
            }

            _engine = MakeLoop("loop_motor");
            _rumble = MakeLoop("loop_turbulencia");
            _music = MakeLoop("loop_musica");
            _engine.Play();
            _music.Play();
            ApplySettings(settings);

            session.ResultsReady += r => Play(r.Victory ? AudioCatalog.Victory : AudioCatalog.Defeat);
            scenario.PassengersCreated += _ => SubscribePassengers();
            SubscribePassengers();
            player.Stumbled += () => Play(AudioCatalog.Slip, player.transform.position);
            interactor.Interacted += OnInteracted;
            CarryableItem.Impact += OnImpact;
        }

        public void ApplySettings(SkyCrew.Domain.Persistence.GameSettings settings)
        {
            _router.ApplySettings(settings);
        }

        private AudioSource MakeLoop(string clipId)
        {
            var src = new GameObject("Loop_" + clipId).AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.clip = ProceduralClips.Get(clipId);
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        private void SubscribePassengers()
        {
            foreach (var p in _session.Passengers)
            {
                p.ServiceRequested -= OnServiceRequested;
                p.ServiceRequested += OnServiceRequested;
            }
        }

        private void OnServiceRequested(PassengerBrain p, ServiceKind kind) => Play(AudioCatalog.Request);

        private void OnInteracted(IInteractable target, InteractionVerb verb)
        {
            Vector3 at = target.InteractionPoint;
            switch (verb)
            {
                case InteractionVerb.PickUp:
                case InteractionVerb.Dispense:
                    Play(AudioCatalog.PickUp, at);
                    break;
                case InteractionVerb.PlaceInZone:
                case InteractionVerb.Dispose:
                case InteractionVerb.Deliver:
                    Play(AudioCatalog.Place, at);
                    break;
                case InteractionVerb.Talk:
                    Play(AudioCatalog.Talk, at);
                    break;
                case InteractionVerb.ApplyItem:
                    Play(AudioCatalog.Scrub, at);
                    break;
                case InteractionVerb.Use:
                    Play(AudioCatalog.Knock, at);
                    break;
            }
        }

        private void OnImpact(Vector3 position, float strength) => Play(AudioCatalog.Impact, position, Mathf.Clamp01(strength / 4f));

        private void OnTaskStateChanged(TaskRuntime task, TaskState previous)
        {
            switch (task.State)
            {
                case TaskState.Active: Play(AudioCatalog.NewTask); break;
                case TaskState.Completed: Play(AudioCatalog.TaskCompleted); break;
                case TaskState.Failed: Play(AudioCatalog.TaskFailed); break;
            }
        }

        private void OnEventStarted(ActiveEvent e) => Play(AudioCatalog.EventAlert);

        /// <summary>Toca um som (posicional se <paramref name="position"/> for informado).</summary>
        public bool Play(string cueId, Vector3? position = null, float intensity = 1f)
        {
            if (_router == null || !_router.TryPlay(cueId, Time.unscaledTime, out var request, intensity))
            {
                return false;
            }

            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.Stop();
            src.clip = ProceduralClips.Get(cueId);
            src.volume = request.Volume;
            src.pitch = request.Pitch;
            src.spatialBlend = position.HasValue ? 1f : 0f;
            if (position.HasValue)
            {
                src.transform.position = position.Value;
            }

            src.Play();
            return true;
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            // A sessao recria tarefas/eventos ao reiniciar: reinscreve quando a instancia muda.
            if (_tasks != _session.Tasks)
            {
                if (_tasks != null) _tasks.StateChanged -= OnTaskStateChanged;
                _tasks = _session.Tasks;
                _tasks.StateChanged += OnTaskStateChanged;
            }

            if (_events != _session.Events)
            {
                if (_events != null) _events.Started -= OnEventStarted;
                _events = _session.Events;
                _events.Started += OnEventStarted;
            }

            bool seatbelt = _session.SeatbeltSignOn;
            if (seatbelt && !_seatbelt)
            {
                Play(AudioCatalog.Seatbelt);
            }

            _seatbelt = seatbelt;

            bool flying = _session.State.Current == MatchState.InFlight;
            float turbulence = _session.TurbulenceIntensity;
            _engine.volume = _router.EffectiveVolume(AudioChannel.Ambience) * (flying ? 0.6f : 0.25f);
            _engine.pitch = flying ? 1f + 0.05f * turbulence : 0.85f;
            if (turbulence > 0f && !_rumble.isPlaying) _rumble.Play();
            if (turbulence <= 0f && _rumble.isPlaying) _rumble.Stop();
            _rumble.volume = _router.EffectiveVolume(AudioChannel.Ambience) * Mathf.Clamp01(turbulence);
            bool calm = _session.State.Current != MatchState.InFlight || turbulence <= 0f;
            _music.volume = Mathf.MoveTowards(_music.volume, _router.EffectiveVolume(AudioChannel.Music) * (calm ? 0.5f : 0.15f),
                                              Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            CarryableItem.Impact -= OnImpact;
            if (_tasks != null) _tasks.StateChanged -= OnTaskStateChanged;
            if (_events != null) _events.Started -= OnEventStarted;
        }
    }
}
