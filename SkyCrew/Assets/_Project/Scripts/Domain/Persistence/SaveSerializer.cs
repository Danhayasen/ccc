using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkyCrew.Domain.Persistence
{
    public enum SaveLoadStatus
    {
        /// <summary>Lido e valido.</summary>
        Ok,

        /// <summary>Lido de uma versao antiga e convertido.</summary>
        Migrated,

        /// <summary>Nao havia arquivo: valores padrao.</summary>
        Missing,

        /// <summary>Arquivo invalido/corrompido: valores padrao (o original e preservado como backup).</summary>
        Corrupted,

        /// <summary>Versao mais nova que este jogo conhece: valores padrao, original preservado.</summary>
        FromNewerVersion,
    }

    public readonly struct SaveLoadResult
    {
        public SaveLoadResult(SaveData data, SaveLoadStatus status, string detail)
        {
            Data = data;
            Status = status;
            Detail = detail;
        }

        public SaveData Data { get; }
        public SaveLoadStatus Status { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Formato texto simples, legivel e versionado (sem dependencia de JSON da Unity):
    /// <code>
    /// SKYCREW_SAVE
    /// versao=2
    /// chave=valor
    /// ...
    /// checksum=XXXXXXXX     (FNV-1a 32 bits de todas as linhas anteriores)
    /// </code>
    /// Valores usam cultura invariante; listas sao separadas por ';'.
    /// </summary>
    public static class SaveSerializer
    {
        public const string Header = "SKYCREW_SAVE";

        public static string Serialize(SaveData d)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            Line(sb, "versao", d.Version.ToString(CultureInfo.InvariantCulture));
            Line(sb, "config.volume_geral", F(d.Settings.MasterVolume));
            Line(sb, "config.volume_musica", F(d.Settings.MusicVolume));
            Line(sb, "config.volume_efeitos", F(d.Settings.SfxVolume));
            Line(sb, "config.sensibilidade_mouse", F(d.Settings.MouseSensitivity));
            Line(sb, "config.inverter_y", d.Settings.InvertY ? "1" : "0");
            Line(sb, "stats.voos", I(d.Stats.FlightsPlayed));
            Line(sb, "stats.vitorias", I(d.Stats.FlightsWon));
            Line(sb, "stats.tarefas_concluidas", I(d.Stats.TasksCompleted));
            Line(sb, "stats.tarefas_falhas", I(d.Stats.TasksFailed));
            Line(sb, "stats.estrelas", I(d.Stats.TotalStars));
            Line(sb, "stats.melhor_pontuacao", I(d.Stats.BestScore));
            Line(sb, "stats.pontos_totais", d.Stats.TotalScore.ToString(CultureInfo.InvariantCulture));
            Line(sb, "desbloqueios", string.Join(";", d.Unlocks));
            Line(sb, "equipado", d.EquippedOutfit ?? "");
            Line(sb, "partidas_processadas", string.Join(";", d.ProcessedMatches));
            string body = sb.ToString();
            return body + "checksum=" + Checksum(body) + "\n";
        }

        public static SaveLoadResult Deserialize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Fail(SaveLoadStatus.Corrupted, "arquivo vazio");
            }

            text = text.Replace("\r\n", "\n");
            int checksumAt = text.LastIndexOf("checksum=", StringComparison.Ordinal);
            if (!text.StartsWith(Header + "\n", StringComparison.Ordinal) || checksumAt < 0)
            {
                return Fail(SaveLoadStatus.Corrupted, "cabecalho ou checksum ausente");
            }

            string body = text.Substring(0, checksumAt);
            string stored = text.Substring(checksumAt + "checksum=".Length).Trim();
            if (!string.Equals(stored, Checksum(body), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(SaveLoadStatus.Corrupted, "checksum nao confere");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in body.Split('\n'))
            {
                if (raw.Length == 0 || raw == Header)
                {
                    continue;
                }

                int eq = raw.IndexOf('=');
                if (eq <= 0)
                {
                    return Fail(SaveLoadStatus.Corrupted, $"linha invalida: '{raw}'");
                }

                values[raw.Substring(0, eq)] = raw.Substring(eq + 1);
            }

            if (!values.TryGetValue("versao", out string versionText)
                || !int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                || version < 1)
            {
                return Fail(SaveLoadStatus.Corrupted, "versao invalida");
            }

            if (version > SaveData.CurrentVersion)
            {
                return Fail(SaveLoadStatus.FromNewerVersion, $"versao {version} > {SaveData.CurrentVersion}");
            }

            try
            {
                var d = SaveData.CreateDefault();
                d.Settings.MasterVolume = Clamp01(GetF(values, "config.volume_geral", d.Settings.MasterVolume));
                d.Settings.MusicVolume = Clamp01(GetF(values, "config.volume_musica", d.Settings.MusicVolume));
                d.Settings.SfxVolume = Clamp01(GetF(values, "config.volume_efeitos", d.Settings.SfxVolume));
                d.Settings.MouseSensitivity = Math.Max(0.1f, Math.Min(5f, GetF(values, "config.sensibilidade_mouse", 1f)));
                d.Settings.InvertY = Get(values, "config.inverter_y", "0") == "1";

                foreach (string u in Split(Get(values, "desbloqueios", "")))
                {
                    d.Unlocks.Add(u);
                }

                d.EquippedOutfit = Get(values, "equipado", d.EquippedOutfit);
                if (!d.Unlocks.Contains(d.EquippedOutfit))
                {
                    d.EquippedOutfit = "uniforme_azul"; // nunca equipar algo bloqueado
                }

                if (version >= 2)
                {
                    d.Stats.FlightsPlayed = NonNegative(GetI(values, "stats.voos"));
                    d.Stats.FlightsWon = NonNegative(GetI(values, "stats.vitorias"));
                    d.Stats.TasksCompleted = NonNegative(GetI(values, "stats.tarefas_concluidas"));
                    d.Stats.TasksFailed = NonNegative(GetI(values, "stats.tarefas_falhas"));
                    d.Stats.TotalStars = NonNegative(GetI(values, "stats.estrelas"));
                    d.Stats.BestScore = NonNegative(GetI(values, "stats.melhor_pontuacao"));
                    d.Stats.TotalScore = Math.Max(0L, long.Parse(Get(values, "stats.pontos_totais", "0"), CultureInfo.InvariantCulture));
                    d.ProcessedMatches.AddRange(Split(Get(values, "partidas_processadas", "")));
                }
                else
                {
                    // Migracao v1 -> v2: a v1 guardava so "estrelas" e "voos" na raiz.
                    d.Stats.TotalStars = NonNegative(GetI(values, "estrelas"));
                    d.Stats.FlightsPlayed = NonNegative(GetI(values, "voos"));
                }

                if (d.Stats.FlightsWon > d.Stats.FlightsPlayed)
                {
                    return Fail(SaveLoadStatus.Corrupted, "estatisticas inconsistentes");
                }

                d.Version = SaveData.CurrentVersion;
                return new SaveLoadResult(d, version < SaveData.CurrentVersion ? SaveLoadStatus.Migrated : SaveLoadStatus.Ok,
                                          $"versao {version}");
            }
            catch (FormatException e)
            {
                return Fail(SaveLoadStatus.Corrupted, "valor invalido: " + e.Message);
            }
            catch (OverflowException e)
            {
                return Fail(SaveLoadStatus.Corrupted, "valor fora do intervalo: " + e.Message);
            }
        }

        /// <summary>FNV-1a 32 bits em hexadecimal (deteccao de corrupcao, nao seguranca).</summary>
        public static string Checksum(string text)
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 16777619;
            }

            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }

        private static SaveLoadResult Fail(SaveLoadStatus status, string detail)
            => new SaveLoadResult(SaveData.CreateDefault(), status, detail);

        private static void Line(StringBuilder sb, string key, string value)
        {
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                throw new ArgumentException($"Valor de '{key}' nao pode ter quebra de linha");
            }

            sb.Append(key).Append('=').Append(value).Append('\n');
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static int NonNegative(int v) => v < 0 ? 0 : v;

        private static string Get(Dictionary<string, string> v, string key, string fallback)
            => v.TryGetValue(key, out string s) ? s : fallback;

        private static int GetI(Dictionary<string, string> v, string key)
            => int.Parse(Get(v, key, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static float GetF(Dictionary<string, string> v, string key, float fallback)
        {
            string s = Get(v, key, null);
            if (s == null)
            {
                return fallback;
            }

            float f = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                throw new FormatException($"{key} nao e numero finito");
            }

            return f;
        }

        private static IEnumerable<string> Split(string list)
        {
            foreach (string part in list.Split(';'))
            {
                string p = part.Trim();
                if (p.Length > 0)
                {
                    yield return p;
                }
            }
        }
    }
}
