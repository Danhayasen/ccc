using System;
using System.IO;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.Persistence;
using SkyCrew.Domain.Progression;
using UnityEngine;

namespace SkyCrew.Core
{
    /// <summary>Arquivos em Application.persistentDataPath, com gravacao atomica (temporario + troca).</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _folder;

        public FileSaveStorage(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
        }

        private string PathOf(string name) => Path.Combine(_folder, name);

        public bool Exists(string name) => File.Exists(PathOf(name));

        public string Read(string name) => File.ReadAllText(PathOf(name), System.Text.Encoding.UTF8);

        public void WriteAtomic(string name, string content)
        {
            string target = PathOf(name);
            string temp = target + ".tmp";
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
            if (File.Exists(target))
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // No navegador (sistema de arquivos do Emscripten) File.Replace nao e suportado.
                File.Delete(target);
                File.Move(temp, target);
#else
                File.Replace(temp, target, null);
#endif
            }
            else
            {
                File.Move(temp, target);
            }
        }

        public void Copy(string from, string to) => File.Copy(PathOf(from), PathOf(to), true);
    }

    /// <summary>
    /// Carrega o progresso ao iniciar, aplica o resultado de cada partida (uma vez por id) e salva.
    /// Falhas de disco viram aviso; o jogo continua.
    /// </summary>
    public sealed class SaveManager
    {
        private readonly SaveService _service;

        public SaveManager(ISaveStorage storage)
        {
            _service = new SaveService(storage);
            var result = _service.Load();
            LoadStatus = result.Status;
            if (result.Status == SaveLoadStatus.Corrupted || result.Status == SaveLoadStatus.FromNewerVersion)
            {
                Debug.LogWarning($"[SkyCrew] Save invalido ({result.Detail}). Usando padrao; original copiado para {SaveService.CorruptedBackupName}.");
            }
        }

        /// <summary>Testes podem trocar o disco por memoria (nunca tocar o save real do jogador).</summary>
        public static ISaveStorage StorageOverride { get; set; }

        public static SaveManager CreateDefault()
        {
            if (StorageOverride != null)
            {
                return new SaveManager(StorageOverride);
            }

            try
            {
                return new SaveManager(new FileSaveStorage(Application.persistentDataPath));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[SkyCrew] Sem acesso ao disco, progresso so na memoria: " + e.Message);
                return new SaveManager(new MemorySaveStorage());
            }
        }

        public SaveData Data => _service.Current;
        public SaveLoadStatus LoadStatus { get; }
        public ProgressionReport LastReport { get; private set; }
        public string LastSaveError => _service.LastError;

        public ProgressionReport ApplyMatch(MatchResults results, string matchId)
        {
            LastReport = ProgressionService.Apply(_service.Current, results, matchId);
            if (LastReport.Applied && !_service.Save())
            {
                Debug.LogWarning("[SkyCrew] Nao foi possivel salvar o progresso: " + _service.LastError);
            }

            return LastReport;
        }

        public bool SaveSettings() => _service.Save();
    }
}
