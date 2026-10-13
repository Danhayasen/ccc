using System.Linq;
using NUnit.Framework;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.Persistence;
using SkyCrew.Domain.Progression;
using SkyCrew.Domain.Scoring;

namespace SkyCrew.Tests.EditMode
{
    public class SaveTests
    {
        private static SaveData Sample()
        {
            var d = SaveData.CreateDefault();
            d.Settings.MasterVolume = 0.35f;
            d.Settings.MouseSensitivity = 1.75f;
            d.Settings.InvertY = true;
            d.Stats.FlightsPlayed = 7;
            d.Stats.FlightsWon = 5;
            d.Stats.TotalStars = 11;
            d.Stats.BestScore = 4321;
            d.Stats.TotalScore = 20000;
            d.Unlocks.Add("lenco_amarelo");
            d.EquippedOutfit = "lenco_amarelo";
            d.ProcessedMatches.Add("m1");
            d.ProcessedMatches.Add("m2");
            return d;
        }

        [Test]
        public void IdaEVolta()
        {
            var original = Sample();
            var r = SaveSerializer.Deserialize(SaveSerializer.Serialize(original));
            Assert.AreEqual(SaveLoadStatus.Ok, r.Status, r.Detail);
            Assert.AreEqual(0.35f, r.Data.Settings.MasterVolume);
            Assert.AreEqual(1.75f, r.Data.Settings.MouseSensitivity);
            Assert.IsTrue(r.Data.Settings.InvertY);
            Assert.AreEqual(5, r.Data.Stats.FlightsWon);
            Assert.AreEqual(4321, r.Data.Stats.BestScore);
            Assert.AreEqual(20000L, r.Data.Stats.TotalScore);
            CollectionAssert.AreEquivalent(original.Unlocks, r.Data.Unlocks);
            Assert.AreEqual("lenco_amarelo", r.Data.EquippedOutfit);
            CollectionAssert.AreEqual(new[] { "m1", "m2" }, r.Data.ProcessedMatches);
        }

        [Test]
        public void IndependenteDaCulturaDoSistema()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
                string text = SaveSerializer.Serialize(Sample());
                StringAssert.Contains("config.volume_geral=0.35", text, "decimal com ponto, sempre");
                Assert.AreEqual(0.35f, SaveSerializer.Deserialize(text).Data.Settings.MasterVolume);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [TestCase("")]
        [TestCase("lixo qualquer")]
        [TestCase("SKYCREW_SAVE\nversao=2\n")]
        public void InvalidoViraPadrao(string text)
        {
            var r = SaveSerializer.Deserialize(text);
            Assert.AreEqual(SaveLoadStatus.Corrupted, r.Status);
            Assert.AreEqual(0, r.Data.Stats.FlightsPlayed);
            Assert.IsTrue(r.Data.Unlocks.Contains("uniforme_azul"));
        }

        [Test]
        public void ByteAlteradoEDetectado()
        {
            string text = SaveSerializer.Serialize(Sample()).Replace("stats.estrelas=11", "stats.estrelas=99");
            var r = SaveSerializer.Deserialize(text);
            Assert.AreEqual(SaveLoadStatus.Corrupted, r.Status);
            StringAssert.Contains("checksum", r.Detail);
        }

        private static string WithChecksum(string body) => body + "checksum=" + SaveSerializer.Checksum(body) + "\n";

        [Test]
        public void MigraDaVersao1()
        {
            string v1 = WithChecksum("SKYCREW_SAVE\nversao=1\nestrelas=4\nvoos=3\ndesbloqueios=uniforme_azul;lenco_amarelo\n");
            var r = SaveSerializer.Deserialize(v1);
            Assert.AreEqual(SaveLoadStatus.Migrated, r.Status);
            Assert.AreEqual(4, r.Data.Stats.TotalStars);
            Assert.AreEqual(3, r.Data.Stats.FlightsPlayed);
            Assert.AreEqual(SaveData.CurrentVersion, r.Data.Version);
            Assert.IsTrue(r.Data.Unlocks.Contains("lenco_amarelo"));
        }

        [Test]
        public void VersaoFuturaNaoEApagada()
        {
            string v9 = WithChecksum("SKYCREW_SAVE\nversao=9\n");
            var storage = new MemorySaveStorage();
            storage.Files[SaveService.FileName] = v9;
            var service = new SaveService(storage);
            Assert.AreEqual(SaveLoadStatus.FromNewerVersion, service.Load().Status);
            Assert.AreEqual(v9, storage.Files[SaveService.CorruptedBackupName], "original preservado");
        }

        [Test]
        public void ValoresAbsurdosSaoLimitadosOuRecusados()
        {
            string body = "SKYCREW_SAVE\nversao=2\nconfig.volume_geral=7\nconfig.sensibilidade_mouse=-3\nstats.voos=1\nstats.vitorias=1\n";
            var r = SaveSerializer.Deserialize(WithChecksum(body));
            Assert.AreEqual(SaveLoadStatus.Ok, r.Status);
            Assert.AreEqual(1f, r.Data.Settings.MasterVolume);
            Assert.AreEqual(0.1f, r.Data.Settings.MouseSensitivity);

            string inconsistent = "SKYCREW_SAVE\nversao=2\nstats.voos=1\nstats.vitorias=5\n";
            Assert.AreEqual(SaveLoadStatus.Corrupted, SaveSerializer.Deserialize(WithChecksum(inconsistent)).Status);

            string nan = "SKYCREW_SAVE\nversao=2\nconfig.volume_geral=NaN\n";
            Assert.AreEqual(SaveLoadStatus.Corrupted, SaveSerializer.Deserialize(WithChecksum(nan)).Status);
        }

        [Test]
        public void ItemEquipadoBloqueadoVoltaAoPadrao()
        {
            string body = "SKYCREW_SAVE\nversao=2\ndesbloqueios=uniforme_azul\nequipado=quepe_dourado\n";
            var r = SaveSerializer.Deserialize(WithChecksum(body));
            Assert.AreEqual("uniforme_azul", r.Data.EquippedOutfit);
        }

        [Test]
        public void ServicoSemArquivoECorrompido()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService(storage);
            Assert.AreEqual(SaveLoadStatus.Missing, service.Load().Status);
            Assert.IsNotNull(service.Current);

            storage.Files[SaveService.FileName] = "estragado";
            Assert.AreEqual(SaveLoadStatus.Corrupted, service.Load().Status);
            Assert.AreEqual("estragado", storage.Files[SaveService.CorruptedBackupName]);
        }

        [Test]
        public void PersisteEntreSessoesEFalhaDeDiscoNaoQuebra()
        {
            var storage = new MemorySaveStorage();
            var a = new SaveService(storage);
            a.Load();
            a.Current.Stats.TotalStars = 6;
            Assert.IsTrue(a.Save());

            var b = new SaveService(storage); // "reiniciou o jogo"
            Assert.AreEqual(SaveLoadStatus.Ok, b.Load().Status);
            Assert.AreEqual(6, b.Current.Stats.TotalStars);

            b.Current.Stats.TotalStars = 7;
            storage.FailNextWrite = true;
            Assert.IsFalse(b.Save());
            Assert.IsNotNull(b.LastError);
            Assert.AreEqual(6, SaveSerializer.Deserialize(storage.Files[SaveService.FileName]).Data.Stats.TotalStars,
                            "arquivo anterior intacto");
            Assert.IsTrue(b.Save());
        }

        [Test]
        public void SemSegredosNoArquivo()
        {
            string text = SaveSerializer.Serialize(Sample()).ToLowerInvariant();
            foreach (string forbidden in new[] { "token", "senha", "password", "secret", "email" })
            {
                StringAssert.DoesNotContain(forbidden, text);
            }
        }
    }

    public class ProgressionTests
    {
        private static MatchResults Win(int stars, int total = 3000, int tasks = 10)
        {
            return new MatchResults
            {
                Victory = true,
                Score = new ScoreBreakdown(total, 0, 0, total, stars),
                TasksCompleted = tasks,
            };
        }

        [Test]
        public void MesmaPartidaNaoRendeDuasVezes()
        {
            var save = SaveData.CreateDefault();
            var r1 = ProgressionService.Apply(save, Win(3), "partida-1");
            var r2 = ProgressionService.Apply(save, Win(3), "partida-1");
            Assert.IsTrue(r1.Applied);
            Assert.IsFalse(r2.Applied);
            Assert.AreEqual(3, save.Stats.TotalStars);
            Assert.AreEqual(1, save.Stats.FlightsPlayed);
        }

        [Test]
        public void DesbloqueiaPorEstrelasEVitorias()
        {
            var save = SaveData.CreateDefault();
            var r = ProgressionService.Apply(save, Win(2), "a");
            CollectionAssert.AreEqual(new[] { "lenco_amarelo" }, r.NewUnlocks.Select(u => u.Id));
            for (int i = 0; i < 4; i++)
            {
                ProgressionService.Apply(save, Win(3), "b" + i);
            }

            Assert.AreEqual(14, save.Stats.TotalStars);
            Assert.IsTrue(save.Unlocks.Contains("uniforme_vermelho"));
            Assert.IsTrue(save.Unlocks.Contains("quepe_dourado"));
            Assert.IsTrue(save.Unlocks.Contains("modo_turbulencia"), "12 estrelas e 3 vitorias");
            Assert.IsFalse(save.Unlocks.Contains("uniforme_verde"));
        }

        [Test]
        public void DerrotaContaVooMasNaoDaEstrelas()
        {
            var save = SaveData.CreateDefault();
            var loss = new MatchResults { Victory = false, Score = new ScoreBreakdown(500, 100, 0, 400, 0), TasksFailed = 2 };
            var r = ProgressionService.Apply(save, loss, "x");
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(1, save.Stats.FlightsPlayed);
            Assert.AreEqual(0, save.Stats.FlightsWon);
            Assert.AreEqual(0, save.Stats.TotalStars);
            Assert.AreEqual(2, save.Stats.TasksFailed);
        }

        [Test]
        public void MelhorPontuacao()
        {
            var save = SaveData.CreateDefault();
            Assert.IsTrue(ProgressionService.Apply(save, Win(1, 2500), "a").NewBestScore);
            Assert.IsFalse(ProgressionService.Apply(save, Win(1, 2000), "b").NewBestScore);
            Assert.AreEqual(2500, save.Stats.BestScore);
        }

        [Test]
        public void ListaDePartidasTemLimite()
        {
            var save = SaveData.CreateDefault();
            for (int i = 0; i < SaveData.ProcessedMatchesKept + 10; i++)
            {
                ProgressionService.Apply(save, Win(0), "m" + i);
            }

            Assert.AreEqual(SaveData.ProcessedMatchesKept, save.ProcessedMatches.Count);
        }

        [Test]
        public void SoEquipaDesbloqueado()
        {
            var save = SaveData.CreateDefault();
            Assert.IsFalse(ProgressionService.TryEquip(save, "uniforme_verde"));
            Assert.AreEqual("uniforme_azul", save.EquippedOutfit);
            save.Unlocks.Add("uniforme_verde");
            Assert.IsTrue(ProgressionService.TryEquip(save, "uniforme_verde"));
        }

        [Test]
        public void ProgressoSobreviveAoSalvamento()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService(storage);
            service.Load();
            ProgressionService.Apply(service.Current, Win(3), "p1");
            service.Save();
            var again = new SaveService(storage);
            again.Load();
            Assert.IsFalse(ProgressionService.Apply(again.Current, Win(3), "p1").Applied, "nem depois de reiniciar o jogo");
            Assert.AreEqual(3, again.Current.Stats.TotalStars);
        }
    }
}
