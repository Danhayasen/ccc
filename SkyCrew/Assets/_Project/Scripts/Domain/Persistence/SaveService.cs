using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.Persistence
{
    /// <summary>Armazenamento abstrato (arquivo na Unity, memoria nos testes).</summary>
    public interface ISaveStorage
    {
        bool Exists(string name);
        string Read(string name);

        /// <summary>Grava de forma atomica: ou o arquivo antigo continua inteiro, ou o novo fica inteiro.</summary>
        void WriteAtomic(string name, string content);

        void Copy(string from, string to);
    }

    /// <summary>Armazenamento em memoria (testes e plataformas sem disco).</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Simula falha de disco na proxima gravacao.</summary>
        public bool FailNextWrite;

        public bool Exists(string name) => Files.ContainsKey(name);

        public string Read(string name) => Files.TryGetValue(name, out string s) ? s : null;

        public void WriteAtomic(string name, string content)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new System.IO.IOException("falha simulada de disco");
            }

            Files[name] = content;
        }

        public void Copy(string from, string to)
        {
            if (Files.TryGetValue(from, out string s))
            {
                Files[to] = s;
            }
        }
    }

    /// <summary>
    /// Carrega e salva o progresso. Arquivo ausente/corrompido nunca impede o jogo de iniciar:
    /// usa valores padrao e preserva o arquivo ruim como ".corrompido" para diagnostico.
    /// </summary>
    public sealed class SaveService
    {
        public const string FileName = "skycrew.save";
        public const string CorruptedBackupName = "skycrew.save.corrompido";

        private readonly ISaveStorage _storage;

        public SaveService(ISaveStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public SaveData Current { get; private set; } = SaveData.CreateDefault();
        public SaveLoadStatus LastLoadStatus { get; private set; } = SaveLoadStatus.Missing;
        public string LastError { get; private set; }

        public SaveLoadResult Load()
        {
            SaveLoadResult result;
            if (!_storage.Exists(FileName))
            {
                result = new SaveLoadResult(SaveData.CreateDefault(), SaveLoadStatus.Missing, "sem arquivo");
            }
            else
            {
                string text;
                try
                {
                    text = _storage.Read(FileName);
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    text = null;
                    LastError = e.Message;
                }

                result = SaveSerializer.Deserialize(text);
                if (result.Status == SaveLoadStatus.Corrupted || result.Status == SaveLoadStatus.FromNewerVersion)
                {
                    TryBackup();
                }
            }

            Current = result.Data;
            LastLoadStatus = result.Status;
            return result;
        }

        /// <summary>Salva. Retorna false (sem lancar) se o disco falhar; o estado em memoria continua valido.</summary>
        public bool Save()
        {
            try
            {
                _storage.WriteAtomic(FileName, SaveSerializer.Serialize(Current));
                LastError = null;
                return true;
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                LastError = e.Message;
                return false;
            }
        }

        private void TryBackup()
        {
            try
            {
                _storage.Copy(FileName, CorruptedBackupName);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                LastError = e.Message;
            }
        }
    }
}
