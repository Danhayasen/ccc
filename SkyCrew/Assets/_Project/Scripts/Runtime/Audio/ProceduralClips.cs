using System;
using System.Collections.Generic;
using SkyCrew.Domain.Audio;
using UnityEngine;

namespace SkyCrew.Audio
{
    /// <summary>
    /// SONS PROVISORIOS sintetizados em codigo (tons, ruido filtrado, envelopes).
    /// Servem para validar o feedback sonoro sem arquivos de audio; serao trocados por
    /// sons gravados/licenciados na Fase 6 (mesmos ids do AudioCatalog).
    /// </summary>
    public static class ProceduralClips
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static readonly System.Random Noise = new System.Random(7);

        public static AudioClip Get(string id)
        {
            if (Cache.TryGetValue(id, out var clip))
            {
                return clip;
            }

            clip = Build(id);
            Cache[id] = clip;
            return clip;
        }

        private static AudioClip Build(string id)
        {
            switch (id)
            {
                case AudioCatalog.Request: return Notes(id, new[] { 880f, 1175f }, 0.18f, 0.5f);
                case AudioCatalog.NewTask: return Notes(id, new[] { 660f }, 0.2f, 0.4f);
                case AudioCatalog.TaskCompleted: return Notes(id, new[] { 784f, 988f, 1319f }, 0.13f, 0.5f);
                case AudioCatalog.TaskFailed: return Buzz(id, 140f, 0.6f);
                case AudioCatalog.EventAlert: return Notes(id, new[] { 523f, 392f }, 0.3f, 0.6f);
                case AudioCatalog.Seatbelt: return Notes(id, new[] { 1046f, 784f }, 0.45f, 0.4f, bell: true);
                case AudioCatalog.Impact: return Thud(id, 0.22f, 180f);
                case AudioCatalog.Slip: return Slide(id, 0.55f);
                case AudioCatalog.Talk: return Babble(id, 0.45f);
                case AudioCatalog.PickUp: return Notes(id, new[] { 520f }, 0.08f, 0.35f);
                case AudioCatalog.Place: return Thud(id, 0.2f, 120f);
                case AudioCatalog.Knock: return Thud(id, 0.18f, 320f);
                case AudioCatalog.Scrub: return Scrub(id, 0.32f);
                case AudioCatalog.Victory: return Notes(id, new[] { 523f, 659f, 784f, 1046f }, 0.28f, 0.5f);
                case AudioCatalog.Defeat: return Notes(id, new[] { 392f, 330f, 262f }, 0.4f, 0.5f);
                case "loop_motor": return EngineLoop(id, 4f);
                case "loop_turbulencia": return RumbleLoop(id, 3f);
                case "loop_musica": return MusicLoop(id);
                default: throw new ArgumentException("Som sem sintese: " + id);
            }
        }

        private static AudioClip Make(string id, float[] data, bool loop = false)
        {
            var clip = AudioClip.Create("Provisorio_" + id, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Envelope(float t, float length, float attack = 0.01f)
        {
            if (t < attack) return t / attack;
            return Mathf.Clamp01(1f - (t - attack) / Mathf.Max(0.001f, length - attack));
        }

        private static float N() => (float)(Noise.NextDouble() * 2 - 1);

        private static AudioClip Notes(string id, float[] freqs, float noteLength, float volume, bool bell = false)
        {
            int perNote = (int)(Rate * noteLength);
            var data = new float[perNote * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = (float)i / Rate;
                    float env = Envelope(t, noteLength) * (bell ? Mathf.Exp(-t * 4f) * 1.5f : 1f);
                    float w = Mathf.Sin(2 * Mathf.PI * freqs[n] * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * freqs[n] * t);
                    data[n * perNote + i] = w * env * volume * 0.7f;
                }
            }

            return Make(id, data);
        }

        private static AudioClip Buzz(string id, float freq, float length)
        {
            var data = new float[(int)(Rate * length)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float square = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * freq * t));
                data[i] = square * Envelope(t, length) * 0.25f;
            }

            return Make(id, data);
        }

        private static AudioClip Thud(string id, float length, float freq)
        {
            var data = new float[(int)(Rate * length)];
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                low += (N() - low) * 0.08f; // ruido filtrado (grave)
                float decay = Mathf.Exp(-t * 18f);
                data[i] = (Mathf.Sin(2 * Mathf.PI * freq * t * (1f - t)) * 0.7f + low * 2f) * decay * 0.8f;
            }

            return Make(id, data);
        }

        private static AudioClip Slide(string id, float length)
        {
            var data = new float[(int)(Rate * length)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float f = Mathf.Lerp(900f, 200f, t / length); // "fiuuu" descendo
                data[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Envelope(t, length, 0.02f) * 0.4f;
            }

            return Make(id, data);
        }

        private static AudioClip Babble(string id, float length)
        {
            var data = new float[(int)(Rate * length)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float syllable = Mathf.Abs(Mathf.Sin(2 * Mathf.PI * 7f * t)); // "blá-blá"
                float f = 220f + 60f * Mathf.Sin(2 * Mathf.PI * 3f * t);
                data[i] = (Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 2.7f * f * t)) * syllable
                          * Envelope(t, length, 0.03f) * 0.35f;
            }

            return Make(id, data);
        }

        private static AudioClip Scrub(string id, float length)
        {
            var data = new float[(int)(Rate * length)];
            float hp = 0f, prev = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N();
                hp = 0.9f * (hp + n - prev); // ruido agudo (esfregar)
                prev = n;
                float strokes = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 9f * t);
                data[i] = hp * strokes * Envelope(t, length, 0.02f) * 0.3f;
            }

            return Make(id, data);
        }

        private static AudioClip EngineLoop(string id, float length)
        {
            var data = new float[(int)(Rate * length)];
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                low += (N() - low) * 0.02f;
                // 90 Hz cabe numero inteiro de vezes em 4 s: o loop nao "estala"
                data[i] = low * 0.6f + Mathf.Sin(2 * Mathf.PI * 90f * t) * 0.05f;
            }

            return Make(id, CrossfadeEnds(data), true);
        }

        private static AudioClip RumbleLoop(string id, float length)
        {
            var data = new float[(int)(Rate * length)];
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                low += (N() - low) * 0.01f;
                float shake = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 2f * t);
                data[i] = low * 3f * shake;
            }

            return Make(id, CrossfadeEnds(data), true);
        }

        private static AudioClip MusicLoop(string id)
        {
            // Arpejo pentatonico tranquilo (8 s, 120 bpm). Placeholder de musica.
            float[] scale = { 392f, 440f, 523f, 587f, 659f, 784f };
            int[] pattern = { 0, 2, 4, 2, 1, 3, 5, 3, 0, 2, 4, 5, 4, 2, 1, 0 };
            float step = 0.5f;
            var data = new float[(int)(Rate * step * pattern.Length)];
            for (int s = 0; s < pattern.Length; s++)
            {
                float f = scale[pattern[s]];
                int start = (int)(s * step * Rate);
                for (int i = 0; i < step * Rate; i++)
                {
                    float t = (float)i / Rate;
                    float env = Mathf.Exp(-t * 3f);
                    data[start + i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.2f * Mathf.Sin(2 * Mathf.PI * f * 2 * t)) * env * 0.18f;
                    if (s % 4 == 0)
                    {
                        data[start + i] += Mathf.Sin(2 * Mathf.PI * f / 2 * t) * env * 0.12f; // baixo
                    }
                }
            }

            return Make(id, data, true);
        }

        /// <summary>Funde o fim no comeco para o loop nao "estalar".</summary>
        private static float[] CrossfadeEnds(float[] data)
        {
            int fade = Math.Min(Rate / 10, data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                data[i] = data[i] * k + data[data.Length - fade + i] * (1f - k);
            }

            Array.Resize(ref data, data.Length - fade);
            return data;
        }
    }
}
