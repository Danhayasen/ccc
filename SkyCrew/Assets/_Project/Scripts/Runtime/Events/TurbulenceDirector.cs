using System.Collections.Generic;
using SkyCrew.Aircraft;
using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Interaction;
using SkyCrew.Physics;
using SkyCrew.Player;
using UnityEngine;

namespace SkyCrew.Events
{
    /// <summary>
    /// Efeitos da turbulencia no mundo: tremor de camera, impulsos limitados nos objetos,
    /// tropecos do jogador, luzes piscando e lixo caindo. Ao terminar, restaura as luzes.
    /// As regras (quando comeca/termina, quem se assusta) ficam no dominio.
    /// </summary>
    public sealed class TurbulenceDirector : MonoBehaviour
    {
        private MatchSession _session;
        private ThirdPersonCamera _camera;
        private PlayerMotor _player;
        private LevelRefs _level;
        private readonly List<float> _lightBase = new List<float>();
        private float _pulseTimer;
        private bool _active;

        public void Bind(MatchSession session, PrototypeScenario scenario, ThirdPersonCamera cameraRig,
                         PlayerMotor player, LevelRefs level)
        {
            _session = session;
            _camera = cameraRig;
            _player = player;
            _level = level;
            _lightBase.Clear();
            foreach (var l in level.Lights)
            {
                _lightBase.Add(l.intensity);
            }

        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            float intensity = _session.TurbulenceIntensity;
            if (intensity <= 0f)
            {
                if (_active)
                {
                    Restore();
                }

                return;
            }

            _active = true;
            float dt = Time.deltaTime;
            _camera.AddShake(intensity * dt * 1.5f);

            for (int i = 0; i < _level.Lights.Count; i++)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 9f, i * 3.1f);
                _level.Lights[i].intensity = _lightBase[i] * Mathf.Lerp(0.55f, 1.05f, flicker);
            }

            _pulseTimer -= dt;
            if (_pulseTimer > 0f)
            {
                return;
            }

            _pulseTimer = Random.Range(0.5f, 1.1f);
            Vector3 jolt = new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), Random.Range(-0.5f, 0.5f)) * intensity;
            foreach (var item in Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
            {
                float scale = item.Body.mass;
                item.ApplyImpulse(jolt * scale * 1.2f, Random.insideUnitSphere * scale * 0.3f);
            }

            _camera.AddShake(0.15f * intensity);
            if (!_player.IsStumbling && Random.value < 0.25f * intensity)
            {
                _player.Stumble(new Vector3(jolt.x, 0f, jolt.z) * 2.5f, 0.45f);
            }
        }

        private void Restore()
        {
            _active = false;
            for (int i = 0; i < _level.Lights.Count; i++)
            {
                _level.Lights[i].intensity = _lightBase[i];
            }
        }

        private void OnDisable()
        {
            if (_level != null)
            {
                Restore();
            }
        }
    }
}
