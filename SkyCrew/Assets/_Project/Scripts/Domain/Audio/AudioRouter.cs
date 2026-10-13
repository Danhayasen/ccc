using System;
using System.Collections.Generic;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Persistence;

namespace SkyCrew.Domain.Audio
{
    public enum AudioChannel
    {
        Music,
        Ambience,
        Physics,
        Interaction,
        Voice,
        UI,
        Alerts,
    }

    /// <summary>Definicao de um som curto (quando pode tocar e com que variacao).</summary>
    public sealed class AudioCue
    {
        public AudioCue(string id, AudioChannel channel, float baseVolume = 1f, float cooldownSeconds = 0.1f,
                        int maxSimultaneous = 2, float volumeVariation = 0.1f, float pitchVariation = 0.08f,
                        float durationSeconds = 0.5f)
        {
            Id = id;
            Channel = channel;
            BaseVolume = Math.Max(0f, baseVolume);
            CooldownSeconds = Math.Max(0f, cooldownSeconds);
            MaxSimultaneous = Math.Max(1, maxSimultaneous);
            VolumeVariation = Math.Max(0f, Math.Min(0.9f, volumeVariation));
            PitchVariation = Math.Max(0f, Math.Min(0.5f, pitchVariation));
            DurationSeconds = Math.Max(0.01f, durationSeconds);
        }

        public string Id { get; }
        public AudioChannel Channel { get; }
        public float BaseVolume { get; }
        public float CooldownSeconds { get; }
        public int MaxSimultaneous { get; }
        public float VolumeVariation { get; }
        public float PitchVariation { get; }
        public float DurationSeconds { get; }
    }

    public readonly struct PlayRequest
    {
        public PlayRequest(string cueId, AudioChannel channel, float volume, float pitch)
        {
            CueId = cueId;
            Channel = channel;
            Volume = volume;
            Pitch = pitch;
        }

        public string CueId { get; }
        public AudioChannel Channel { get; }
        public float Volume { get; }
        public float Pitch { get; }
    }

    /// <summary>
    /// Decide SE e COMO um som toca: volume por canal, limite de repeticao (cooldown),
    /// maximo de instancias simultaneas e pequenas variacoes de volume/tom. Sem Unity: testavel.
    /// </summary>
    public sealed class AudioRouter
    {
        private readonly IRandom _random;
        private readonly Dictionary<string, AudioCue> _cues = new Dictionary<string, AudioCue>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<float>> _endTimes = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        private readonly float[] _channelVolume;

        public AudioRouter(IRandom random, IEnumerable<AudioCue> cues)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _channelVolume = new float[Enum.GetValues(typeof(AudioChannel)).Length];
            for (int i = 0; i < _channelVolume.Length; i++)
            {
                _channelVolume[i] = 1f;
            }

            foreach (var c in cues)
            {
                _cues[c.Id] = c;
            }
        }

        public float MasterVolume { get; set; } = 1f;

        public void SetChannelVolume(AudioChannel channel, float volume)
            => _channelVolume[(int)channel] = MathUtil.Clamp01(volume);

        public float GetChannelVolume(AudioChannel channel) => _channelVolume[(int)channel];

        /// <summary>Volume final de um canal (para loops como ambiente e musica).</summary>
        public float EffectiveVolume(AudioChannel channel) => MathUtil.Clamp01(MasterVolume) * _channelVolume[(int)channel];

        /// <summary>Aplica as configuracoes salvas (efeitos cobre fisica, interacao, interface e alertas).</summary>
        public void ApplySettings(GameSettings s)
        {
            MasterVolume = s.MasterVolume;
            SetChannelVolume(AudioChannel.Music, s.MusicVolume);
            SetChannelVolume(AudioChannel.Ambience, s.AmbienceVolume);
            SetChannelVolume(AudioChannel.Voice, s.VoiceVolume);
            SetChannelVolume(AudioChannel.Physics, s.SfxVolume);
            SetChannelVolume(AudioChannel.Interaction, s.SfxVolume);
            SetChannelVolume(AudioChannel.UI, s.SfxVolume);
            SetChannelVolume(AudioChannel.Alerts, s.SfxVolume);
        }

        public bool HasCue(string id) => _cues.ContainsKey(id);

        public int ActiveCount(string cueId, float now)
        {
            if (!_endTimes.TryGetValue(cueId, out var ends))
            {
                return 0;
            }

            ends.RemoveAll(t => t <= now);
            return ends.Count;
        }

        /// <param name="intensity">Multiplicador extra (ex.: forca do impacto), limitado a [0, 1].</param>
        public bool TryPlay(string cueId, float now, out PlayRequest request, float intensity = 1f)
        {
            request = default;
            if (!_cues.TryGetValue(cueId, out var cue))
            {
                return false;
            }

            if (_lastPlay.TryGetValue(cueId, out float last) && now - last < cue.CooldownSeconds)
            {
                return false; // nao repete o mesmo som em rajada
            }

            if (ActiveCount(cueId, now) >= cue.MaxSimultaneous)
            {
                return false; // sons nao se acumulam indefinidamente
            }

            float variation = 1f + (float)(_random.NextDouble() * 2 - 1) * cue.VolumeVariation;
            float volume = cue.BaseVolume * variation * EffectiveVolume(cue.Channel) * MathUtil.Clamp01(intensity);
            if (volume <= 0.001f)
            {
                return false; // canal mudo: nem ocupa voz
            }

            float pitch = 1f + (float)(_random.NextDouble() * 2 - 1) * cue.PitchVariation;
            _lastPlay[cueId] = now;
            if (!_endTimes.TryGetValue(cueId, out var ends))
            {
                ends = new List<float>();
                _endTimes[cueId] = ends;
            }

            ends.Add(now + cue.DurationSeconds / Math.Max(0.1f, pitch));
            request = new PlayRequest(cueId, cue.Channel, MathUtil.Clamp01(volume), pitch);
            return true;
        }
    }

    /// <summary>Sons do prototipo (os arquivos sao gerados por codigo na Unity: provisorios).</summary>
    public static class AudioCatalog
    {
        public const string Request = "pedido";
        public const string TaskCompleted = "tarefa_concluida";
        public const string TaskFailed = "tarefa_falhou";
        public const string NewTask = "nova_tarefa";
        public const string EventAlert = "alerta_evento";
        public const string Seatbelt = "aviso_cinto";
        public const string Impact = "impacto";
        public const string Slip = "escorregao";
        public const string Talk = "conversa";
        public const string PickUp = "pegar";
        public const string Place = "guardar";
        public const string Knock = "batida";
        public const string Scrub = "esfregar";
        public const string Victory = "vitoria";
        public const string Defeat = "derrota";

        public static IEnumerable<AudioCue> Default()
        {
            yield return new AudioCue(Request, AudioChannel.Voice, 0.8f, cooldownSeconds: 0.8f, durationSeconds: 0.4f);
            yield return new AudioCue(TaskCompleted, AudioChannel.UI, 0.7f, cooldownSeconds: 0.15f, durationSeconds: 0.6f);
            yield return new AudioCue(TaskFailed, AudioChannel.Alerts, 0.8f, cooldownSeconds: 0.3f, durationSeconds: 0.7f);
            yield return new AudioCue(NewTask, AudioChannel.UI, 0.5f, cooldownSeconds: 0.5f, durationSeconds: 0.3f);
            yield return new AudioCue(EventAlert, AudioChannel.Alerts, 0.8f, cooldownSeconds: 2f, maxSimultaneous: 1, durationSeconds: 0.8f);
            yield return new AudioCue(Seatbelt, AudioChannel.Alerts, 0.7f, cooldownSeconds: 5f, maxSimultaneous: 1, durationSeconds: 1f);
            yield return new AudioCue(Impact, AudioChannel.Physics, 0.9f, cooldownSeconds: 0.06f, maxSimultaneous: 4, volumeVariation: 0.25f, pitchVariation: 0.2f, durationSeconds: 0.25f);
            yield return new AudioCue(Slip, AudioChannel.Physics, 1f, cooldownSeconds: 1f, maxSimultaneous: 1, durationSeconds: 0.6f);
            yield return new AudioCue(Talk, AudioChannel.Voice, 0.8f, cooldownSeconds: 0.4f, maxSimultaneous: 1, pitchVariation: 0.25f, durationSeconds: 0.5f);
            yield return new AudioCue(PickUp, AudioChannel.Interaction, 0.6f, cooldownSeconds: 0.1f, durationSeconds: 0.15f);
            yield return new AudioCue(Place, AudioChannel.Interaction, 0.7f, cooldownSeconds: 0.1f, durationSeconds: 0.25f);
            yield return new AudioCue(Knock, AudioChannel.Interaction, 1f, cooldownSeconds: 0.15f, durationSeconds: 0.2f);
            yield return new AudioCue(Scrub, AudioChannel.Interaction, 0.7f, cooldownSeconds: 0.15f, durationSeconds: 0.35f);
            yield return new AudioCue(Victory, AudioChannel.UI, 0.9f, cooldownSeconds: 3f, maxSimultaneous: 1, volumeVariation: 0f, pitchVariation: 0f, durationSeconds: 1.6f);
            yield return new AudioCue(Defeat, AudioChannel.UI, 0.9f, cooldownSeconds: 3f, maxSimultaneous: 1, volumeVariation: 0f, pitchVariation: 0f, durationSeconds: 1.6f);
        }
    }
}
