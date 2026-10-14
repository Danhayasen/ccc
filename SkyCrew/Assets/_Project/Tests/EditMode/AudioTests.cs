using System.Linq;
using NUnit.Framework;
using SkyCrew.Domain.Audio;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Persistence;

namespace SkyCrew.Tests.EditMode
{
    public class AudioRouterTests
    {
        private static AudioRouter NewRouter(ulong seed = 1) => new AudioRouter(new DeterministicRandom(seed), AudioCatalog.Default());

        [Test]
        public void CooldownImpedeRajada()
        {
            var r = NewRouter();
            Assert.IsTrue(r.TryPlay(AudioCatalog.Request, 0f, out _));
            Assert.IsFalse(r.TryPlay(AudioCatalog.Request, 0.5f, out _), "cooldown de 0,8 s");
            Assert.IsTrue(r.TryPlay(AudioCatalog.Request, 0.9f, out _));
        }

        [Test]
        public void LimiteDeInstanciasSimultaneas()
        {
            var r = NewRouter();
            int tocou = 0;
            // 20 impactos em 0,5 s: cooldown 0,06 s e no maximo 4 ao mesmo tempo (duracao 0,25 s)
            for (int i = 0; i < 20; i++)
            {
                if (r.TryPlay(AudioCatalog.Impact, i * 0.025f, out _)) tocou++;
            }

            Assert.LessOrEqual(r.ActiveCount(AudioCatalog.Impact, 0.475f), 4);
            Assert.Less(tocou, 20);
            Assert.Greater(tocou, 2);
        }

        [Test]
        public void VolumeComposto()
        {
            var r = new AudioRouter(new DeterministicRandom(1),
                new[] { new AudioCue("x", AudioChannel.UI, baseVolume: 0.8f, volumeVariation: 0f, pitchVariation: 0f) });
            r.MasterVolume = 0.5f;
            r.SetChannelVolume(AudioChannel.UI, 0.5f);
            Assert.IsTrue(r.TryPlay("x", 0f, out var req));
            Assert.AreEqual(0.2f, req.Volume, 1e-5);
            Assert.AreEqual(1f, req.Pitch, 1e-5);
        }

        [Test]
        public void CanalMudoNaoOcupaVoz()
        {
            var r = NewRouter();
            r.SetChannelVolume(AudioChannel.Voice, 0f);
            Assert.IsFalse(r.TryPlay(AudioCatalog.Talk, 0f, out _));
            Assert.AreEqual(0, r.ActiveCount(AudioCatalog.Talk, 0f));
        }

        [Test]
        public void ConfiguracoesSalvasSaoAplicadas()
        {
            var r = NewRouter();
            var s = new GameSettings { MasterVolume = 0.5f, MusicVolume = 0.2f, AmbienceVolume = 0.3f, SfxVolume = 0.4f, VoiceVolume = 0.6f };
            r.ApplySettings(s);
            Assert.AreEqual(0.1f, r.EffectiveVolume(AudioChannel.Music), 1e-5);
            Assert.AreEqual(0.15f, r.EffectiveVolume(AudioChannel.Ambience), 1e-5);
            Assert.AreEqual(0.2f, r.EffectiveVolume(AudioChannel.Physics), 1e-5);
            Assert.AreEqual(0.2f, r.EffectiveVolume(AudioChannel.Alerts), 1e-5);
            Assert.AreEqual(0.3f, r.EffectiveVolume(AudioChannel.Voice), 1e-5);
        }

        [Test]
        public void VariacaoDentroDosLimitesEReproduzivel()
        {
            var a = NewRouter(5);
            var b = NewRouter(5);
            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(a.TryPlay(AudioCatalog.Impact, i * 1f, out var ra));
                Assert.IsTrue(b.TryPlay(AudioCatalog.Impact, i * 1f, out var rb));
                Assert.AreEqual(ra.Volume, rb.Volume);
                Assert.AreEqual(ra.Pitch, rb.Pitch);
                Assert.That(ra.Pitch, Is.InRange(0.8f, 1.2f));
                Assert.That(ra.Volume, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void SomDesconhecidoEIntensidade()
        {
            var r = NewRouter();
            Assert.IsFalse(r.TryPlay("nao_existe", 0f, out _));
            Assert.IsFalse(r.TryPlay(AudioCatalog.Impact, 0f, out _, intensity: 0f), "impacto fraco demais nao toca");
            Assert.IsTrue(r.TryPlay(AudioCatalog.Impact, 1f, out var forte, intensity: 1f));
            Assert.Greater(forte.Volume, 0f);
        }

        [Test]
        public void CatalogoTemIdsUnicos()
        {
            var ids = AudioCatalog.Default().Select(c => c.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.GreaterOrEqual(ids.Count, 15);
        }
    }
}
