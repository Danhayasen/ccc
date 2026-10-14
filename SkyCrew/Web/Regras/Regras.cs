using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using SkyCrew.Domain.Audio;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.Persistence;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Progression;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Web
{
    /// <summary>
    /// Fachada das regras do jogo para a versao de navegador (three.js). Faz o mesmo papel do
    /// GameBootstrap da Unity: cria a sessao, repassa sinais do mundo 3D e devolve estado e
    /// eventos em JSON. Nenhuma regra nova aqui: tudo vem de SkyCrew.Domain.
    /// </summary>
    public static partial class Regras
    {
        private static readonly List<string> Queue = new List<string>();
        private static readonly MemorySaveStorage Storage = new MemorySaveStorage();
        private static readonly SaveService Save = new SaveService(Storage);
        private static MatchSession _session;
        private static PrototypeScenario _scenario;
        private static string _matchId;
        private static readonly AudioRouter Audio = new AudioRouter(new DeterministicRandom(12345UL ^ 0xA5A5UL), AudioCatalog.Default());

        public static void Main()
        {
        }

        // --- Partida -------------------------------------------------------------------

        [JSExport]
        public static void NewMatch(int seed)
        {
            Queue.Clear();
            _matchId = null;
            _session = new MatchSession(PrototypeScenario.CreateConfig((ulong)(uint)seed));
            _scenario = PrototypeScenario.Install(_session);
            var s = _session;
            s.State.Changed += (prev, next) =>
            {
                if (next == MatchState.Boarding)
                {
                    _matchId = Guid.NewGuid().ToString("N");
                }

                Emit(new Json().Str("type", "state").Str("from", prev.ToString()).Str("to", next.ToString()));
            };
            s.ResultsReady += OnResults;
            s.EquipmentChanged += (id, ok) => Emit(new Json().Str("type", "equipment").Str("id", id).Bool("ok", ok));
            s.Tasks.StateChanged += (t, prev) => Emit(new Json().Str("type", "task").Str("id", t.Id)
                                                                 .Str("name", t.Definition.DisplayName)
                                                                 .Str("state", t.State.ToString()).Str("from", prev.ToString())
                                                                 .Num("reward", t.Definition.Reward).Bool("mandatory", t.Definition.IsMandatory));
            s.Events.Started += e => Emit(new Json().Str("type", "eventStarted").Str("id", e.Definition.Id)
                                                     .Str("name", e.Definition.DisplayName).Str("instance", e.InstanceId));
            s.Events.Ended += (e, cancelled) => Emit(new Json().Str("type", "eventEnded").Str("id", e.Definition.Id)
                                                                .Bool("cancelled", cancelled));
            s.Score.EntryAdded += e => Emit(new Json().Str("type", "score").Num("amount", e.Amount).Str("reason", e.Reason));
            _scenario.WorldRequested += r => Emit(WorldJson("world", r));
            _scenario.WorldCleared += r => Emit(WorldJson("worldCleared", r));
            foreach (var p in s.Passengers)
            {
                p.ServiceRequested += (brain, kind) => Emit(new Json().Str("type", "request").Str("passenger", brain.Id)
                                                                       .Str("service", kind.ToString()));
            }

            s.TryBecomeReady(out _);
            DrainCommands();
        }

        [JSExport]
        public static bool StartBoarding()
        {
            bool ok = _session != null && _session.StartBoarding(out _);
            DrainCommands();
            return ok;
        }

        [JSExport]
        public static void Tick(double dt)
        {
            if (_session == null)
            {
                return;
            }

            _session.Tick((float)dt);
            DrainCommands();
        }

        /// <summary>Encerra a viagem atual como derrota (sair no meio do voo).</summary>
        [JSExport]
        public static void Abandon()
        {
            if (_session != null && _session.State.IsPlayable)
            {
                _session.Finish(false, "Viagem interrompida pelo jogador");
            }
        }

        // --- Corpo dos passageiros -> cerebro ----------------------------------------------

        [JSExport]
        public static void PassengerArrived(string passengerId)
        {
            _session?.GetPassenger(passengerId)?.OnArrived();
            DrainCommands();
        }

        [JSExport]
        public static void PassengerNavigationFailed(string passengerId)
        {
            _session?.GetPassenger(passengerId)?.OnNavigationFailed();
            DrainCommands();
        }

        // --- Acoes do jogador ------------------------------------------------------------

        [JSExport]
        public static bool CanDeliver(string passengerId, string itemType)
            => _session != null && _session.CanDeliverToPassenger(passengerId, itemType);

        [JSExport]
        public static bool Deliver(string passengerId, string itemType)
        {
            bool ok = _session != null && _session.TryDeliverToPassenger(passengerId, itemType);
            DrainCommands();
            return ok;
        }

        [JSExport]
        public static bool Talk(string passengerId)
        {
            bool ok = _session != null && _session.TryTalkToPassenger(passengerId);
            DrainCommands();
            return ok;
        }

        [JSExport]
        public static void PlacedInZone(string itemType, string zoneId, string itemId)
            => _session?.Signal(GameplaySignal.PlacedInZone(itemType, zoneId, "jogador", itemId));

        [JSExport]
        public static void Disposed(string itemType, string itemId)
            => _session?.Signal(GameplaySignal.Disposed(itemType, null, "jogador", itemId));

        [JSExport]
        public static void PerformAction(string actionId, string targetId)
            => _session?.Signal(GameplaySignal.Action(actionId, targetId, "jogador"));

        /// <summary>Valida uma interacao (InteractionRules). Retorna "" se permitida ou o motivo da recusa.</summary>
        [JSExport]
        public static string ValidateInteraction(string verb, double distance, double range, string heldItemType,
                                                 string acceptedItemType, bool available)
        {
            if (_session == null || !Enum.TryParse(verb, out SkyCrew.Domain.Interaction.InteractionVerb v))
            {
                return "Acao desconhecida";
            }

            var check = SkyCrew.Domain.Interaction.InteractionRules.Validate(new SkyCrew.Domain.Interaction.InteractionQuery(
                v, (float)distance, (float)range, _session.State.Current,
                string.IsNullOrEmpty(heldItemType) ? null : heldItemType,
                string.IsNullOrEmpty(acceptedItemType) ? null : acceptedItemType, available));
            return check.Allowed ? "" : check.Reason;
        }

        /// <summary>Melhor alvo (InteractionTargetSelector). Candidatos: "distancia,angulo,prioridade,valido(0/1);...".</summary>
        [JSExport]
        public static int SelectBestTarget(string candidates)
        {
            var list = new List<SkyCrew.Domain.Interaction.InteractionCandidate>();
            if (!string.IsNullOrEmpty(candidates))
            {
                foreach (string c in candidates.Split(';'))
                {
                    string[] f = c.Split(',');
                    list.Add(new SkyCrew.Domain.Interaction.InteractionCandidate(
                        float.Parse(f[0], CultureInfo.InvariantCulture), float.Parse(f[1], CultureInfo.InvariantCulture),
                        int.Parse(f[2], CultureInfo.InvariantCulture), f[3] == "1"));
                }
            }

            return SkyCrew.Domain.Interaction.InteractionTargetSelector.SelectBest(list);
        }

        /// <summary>Diagnostico/testes: forca um evento (mesmas regras de elegibilidade, sem o intervalo global).</summary>
        [JSExport]
        public static string DebugForceEvent(string eventId)
        {
            if (_session == null)
            {
                return "sem partida";
            }

            var ctx = new EventContext(_session.State.Current, _session.FlightTime, _session.Config.Difficulty);
            return _session.Events.TryStart(eventId, ctx, out string reason, ignoreGlobalInterval: true) ? "" : reason;
        }

        [JSExport]
        public static int PassengerPriority(bool reacting, bool canReceiveHeldItem)
            => SkyCrew.Domain.Interaction.InteractionPriorities.Passenger(reacting, canReceiveHeldItem);

        [JSExport]
        public static bool IsEquipmentAvailable(string equipmentId)
            => _session == null || _session.IsEquipmentAvailable(equipmentId);

        // --- Leitura ---------------------------------------------------------------------

        [JSExport]
        public static string DrainEvents()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < Queue.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Queue[i]);
            }

            Queue.Clear();
            return sb.Append(']').ToString();
        }

        [JSExport]
        public static string Snapshot()
        {
            var s = _session;
            if (s == null)
            {
                return "null";
            }

            var j = new Json()
                .Str("state", s.State.Current.ToString())
                .Num("stateTime", s.StateTime)
                .Num("flightTime", s.FlightTime)
                .Num("flightLeft", s.FlightTimeLeft)
                .Num("flightTotal", s.Config.FlightSeconds)
                .Num("boardingLeft", s.BoardingTimeLeft)
                .Bool("seatbelt", s.SeatbeltSignOn)
                .Num("turbulence", s.TurbulenceIntensity)
                .Num("satisfaction", s.AverageSatisfaction)
                .Num("score", s.Score.Total)
                .Num("openRequests", s.OpenRequests)
                .Num("helpers", s.ActiveHelpers)
                .Bool("fridgeOk", s.IsEquipmentAvailable(PrototypeScenario.EquipmentFridge));

            var passengers = new List<string>();
            foreach (var p in s.Passengers)
            {
                var spec = _scenario.GetSpec(p.Id);
                var n = p.Needs;
                passengers.Add(new Json().Str("id", p.Id).Str("name", spec?.Name ?? p.Id).Str("seat", spec?.SeatId)
                    .Str("personality", p.Profile.Personality.ToString()).Str("profile", p.Profile.DisplayName)
                    .Str("state", p.State.ToString()).Str("pending", p.PendingService.ToString())
                    .Bool("atSeat", p.IsAtSeat).Bool("startsSeated", spec != null && spec.StartsSeated)
                    .Num("walkSpeed", p.Profile.WalkSpeed)
                    .Num("satisfaction", n.Satisfaction).Num("patience", n.Patience).Num("comfort", n.Comfort)
                    .Num("fear", n.Fear).Num("thirst", n.Thirst).Num("hunger", n.Hunger).Num("cold", n.Cold)
                    .Num("lavatory", n.Lavatory).ToString());
            }

            j.Raw("passengers", "[" + string.Join(",", passengers) + "]");

            var tasks = new List<string>();
            foreach (var t in s.Tasks.All)
            {
                if (t.State == TaskState.Available)
                {
                    continue;
                }

                var d = t.Definition;
                tasks.Add(new Json().Str("id", t.Id).Str("name", d.DisplayName).Str("description", d.Description)
                    .Str("state", t.State.ToString()).Str("priority", d.Priority.ToString())
                    .Bool("mandatory", d.IsMandatory).Num("progress", t.Progress)
                    .Num("timeLimit", d.TimeLimitSeconds).Num("timeLeft", t.TimeLeft).ToString());
            }

            j.Raw("tasks", "[" + string.Join(",", tasks) + "]");

            var events = new List<string>();
            foreach (var e in s.Events.Active)
            {
                events.Add(new Json().Str("id", e.Definition.Id).Str("name", e.Definition.DisplayName)
                    .Num("remaining", e.Remaining).Num("intensity", e.Intensity).ToString());
            }

            j.Raw("events", "[" + string.Join(",", events) + "]");
            if (s.Results != null)
            {
                j.Raw("results", ResultsJson(s.Results));
            }

            return j.ToString();
        }

        // --- Progresso permanente (texto salvo no localStorage pelo navegador) -----------

        /// <summary>Carrega o progresso a partir do texto salvo (vazio = novo jogador). Retorna o status.</summary>
        [JSExport]
        public static string LoadSave(string text)
        {
            Storage.Files.Clear();
            if (!string.IsNullOrEmpty(text))
            {
                Storage.Files[SaveService.FileName] = text;
            }

            var status = Save.Load().Status.ToString();
            Audio.ApplySettings(Save.Current.Settings);
            return status;
        }

        /// <summary>Texto do save atual (o navegador grava no localStorage).</summary>
        [JSExport]
        public static string SaveText() => Storage.Read(SaveService.FileName) ?? SaveSerializer.Serialize(Save.Current);

        [JSExport]
        public static string Profile()
        {
            var d = Save.Current;
            var st = d.Stats;
            var catalog = new List<string>();
            foreach (var u in ProgressionService.Catalog)
            {
                catalog.Add(new Json().Str("id", u.Id).Str("name", u.DisplayName).Str("category", u.Category.ToString())
                    .Num("stars", u.StarsRequired).Num("wins", u.WinsRequired).Bool("unlocked", d.Unlocks.Contains(u.Id))
                    .ToString());
            }

            var g = d.Settings;
            return new Json()
                .Num("flightsPlayed", st.FlightsPlayed).Num("flightsWon", st.FlightsWon).Num("totalStars", st.TotalStars)
                .Num("bestScore", st.BestScore).Str("uniform", d.EquippedUniform).Str("accessory", d.EquippedAccessory)
                .Raw("settings", new Json().Num("master", g.MasterVolume).Num("music", g.MusicVolume).Num("sfx", g.SfxVolume)
                    .Num("ambience", g.AmbienceVolume).Num("voice", g.VoiceVolume).Num("mouse", g.MouseSensitivity)
                    .Bool("invertY", g.InvertY).ToString())
                .Raw("catalog", "[" + string.Join(",", catalog) + "]")
                .ToString();
        }

        [JSExport]
        public static bool Equip(string unlockId)
        {
            bool ok = ProgressionService.TryEquip(Save.Current, unlockId ?? "");
            if (ok)
            {
                Save.Save();
            }

            return ok;
        }

        [JSExport]
        public static void SetSettings(double master, double music, double sfx, double ambience, double voice,
                                       double mouse, bool invertY)
        {
            var g = Save.Current.Settings;
            g.MasterVolume = Clamp01(master);
            g.MusicVolume = Clamp01(music);
            g.SfxVolume = Clamp01(sfx);
            g.AmbienceVolume = Clamp01(ambience);
            g.VoiceVolume = Clamp01(voice);
            g.MouseSensitivity = (float)Math.Max(0.2, Math.Min(3.0, mouse));
            g.InvertY = invertY;
            Audio.ApplySettings(g);
            Save.Save();
        }

        // --- Audio (AudioRouter do dominio: volume por canal, repeticao e instancias) ----------

        /// <summary>Retorna "volume,pitch" se o som deve tocar agora, ou "" se foi barrado.</summary>
        [JSExport]
        public static string AudioPlay(string cueId, double now, double intensity)
        {
            if (!Audio.TryPlay(cueId, (float)now, out var request, (float)intensity))
            {
                return "";
            }

            return request.Volume.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   request.Pitch.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Volume efetivo de um canal (Master x canal), para os loops.</summary>
        [JSExport]
        public static double AudioChannelVolume(string channel)
            => Enum.TryParse(channel, out AudioChannel c) ? Audio.EffectiveVolume(c) : 0.0;

        private static float Clamp01(double v) => (float)Math.Max(0.0, Math.Min(1.0, v));

        // --- Internos --------------------------------------------------------------------

        private static void OnResults(MatchResults r)
        {
            ProgressionReport report = null;
            if (_matchId != null)
            {
                report = ProgressionService.Apply(Save.Current, r, _matchId);
                if (report.Applied)
                {
                    Save.Save();
                }
            }

            var j = new Json().Str("type", "results").Raw("results", ResultsJson(r));
            if (report != null)
            {
                var unlocks = new List<string>();
                foreach (var u in report.NewUnlocks)
                {
                    unlocks.Add(new Json().Str("id", u.Id).Str("name", u.DisplayName).ToString());
                }

                j.Raw("progress", new Json().Bool("applied", report.Applied).Num("starsGained", report.StarsGained)
                    .Bool("newBest", report.NewBestScore).Raw("unlocks", "[" + string.Join(",", unlocks) + "]").ToString());
            }

            Emit(j);
        }

        private static string ResultsJson(MatchResults r)
        {
            var lines = new List<string>();
            foreach (string l in r.Lines)
            {
                lines.Add(Json.Quote(l));
            }

            return new Json().Bool("victory", r.Victory).Str("reason", r.Reason)
                .Num("total", r.Score.Total).Num("stars", r.Score.Stars).Num("rewards", r.Score.TaskRewards)
                .Num("penalties", r.Score.TaskPenalties).Num("satisfactionBonus", r.Score.SatisfactionBonus)
                .Num("satisfaction", r.AverageSatisfaction).Num("flightTime", r.FlightTime)
                .Num("tasksCompleted", r.TasksCompleted).Num("tasksFailed", r.TasksFailed)
                .Raw("lines", "[" + string.Join(",", lines) + "]").ToString();
        }

        private static Json WorldJson(string type, WorldRequest r)
        {
            var items = new List<string>();
            foreach (string id in r.ItemIds)
            {
                items.Add(Json.Quote(id));
            }

            return new Json().Str("type", type).Str("kind", r.Kind.ToString()).Str("id", r.Id).Str("task", r.TaskId)
                .Str("passenger", r.PassengerId).Raw("items", "[" + string.Join(",", items) + "]");
        }

        private static void DrainCommands()
        {
            if (_session == null)
            {
                return;
            }

            foreach (var p in _session.Passengers)
            {
                while (p.TryDequeueCommand(out var c))
                {
                    Emit(new Json().Str("type", "command").Str("passenger", p.Id).Str("kind", c.Kind.ToString())
                        .Str("destination", c.Destination.ToString()).Str("service", c.Service.ToString()));
                }
            }
        }

        private static void Emit(Json j) => Queue.Add(j.ToString());
    }

    /// <summary>Escritor de objeto JSON minimo (sem reflexao: seguro com o "trimming" do WebAssembly).</summary>
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _first = true;

        private Json Key(string key)
        {
            if (!_first) _sb.Append(',');
            _first = false;
            _sb.Append(Quote(key)).Append(':');
            return this;
        }

        public Json Str(string key, string value)
        {
            Key(key);
            _sb.Append(value == null ? "null" : Quote(value));
            return this;
        }

        public Json Num(string key, double value)
        {
            Key(key);
            _sb.Append(double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("0.####", CultureInfo.InvariantCulture));
            return this;
        }

        public Json Bool(string key, bool value)
        {
            Key(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public Json Raw(string key, string json)
        {
            Key(key);
            _sb.Append(json);
            return this;
        }

        public override string ToString() => _sb.ToString() + "}";

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
