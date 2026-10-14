// Carrega as regras do jogo (C# de SkyCrew.Domain compilado para WebAssembly).
// A pasta regras/_framework e gerada por: dotnet publish SkyCrew/Web/Regras (veja Web/README.md).

export async function carregarRegras() {
  const { dotnet } = await import('../regras/_framework/dotnet.js');
  const runtime = await dotnet.withConfig({ disableIntegrityCheck: true }).create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const R = exports.SkyCrew.Web.Regras;
  return {
    novaPartida: (seed) => R.NewMatch(seed),
    iniciarEmbarque: () => R.StartBoarding(),
    tick: (dt) => R.Tick(dt),
    abandonar: () => R.Abandon(),
    chegou: (id) => R.PassengerArrived(id),
    falhouRota: (id) => R.PassengerNavigationFailed(id),
    podeEntregar: (id, item) => R.CanDeliver(id, item),
    entregar: (id, item) => R.Deliver(id, item),
    conversar: (id) => R.Talk(id),
    guardado: (tipo, zona, itemId) => R.PlacedInZone(tipo, zona, itemId ?? ''),
    descartado: (tipo, itemId) => R.Disposed(tipo, itemId ?? ''),
    acao: (acaoId, alvo) => R.PerformAction(acaoId, alvo),
    equipamentoOk: (id) => R.IsEquipmentAvailable(id),
    forcarEvento: (id) => R.DebugForceEvent(id),
    prioridadePassageiro: (reagindo, podeReceber) => R.PassengerPriority(!!reagindo, !!podeReceber),
    validar: (verbo, dist, alcance, segurado, aceito, disponivel) =>
      R.ValidateInteraction(verbo, dist, alcance, segurado ?? '', aceito ?? '', !!disponivel),
    melhorAlvo: (cands) => R.SelectBestTarget(cands.map(c =>
      `${c.distance.toFixed(3)},${c.angle.toFixed(2)},${c.priority},${c.valid ? 1 : 0}`).join(';')),
    eventos: () => JSON.parse(R.DrainEvents()),
    estado: () => JSON.parse(R.Snapshot()),
    carregarSave: (texto) => R.LoadSave(texto ?? ''),
    textoSave: () => R.SaveText(),
    perfil: () => JSON.parse(R.Profile()),
    equipar: (id) => R.Equip(id ?? ''),
    som: (cue, agora, intensidade = 1) => {
      const r = R.AudioPlay(cue, agora, intensidade);
      if (!r) return null;
      const [volume, pitch] = r.split(',').map(Number);
      return { volume, pitch };
    },
    volumeCanal: (canal) => R.AudioChannelVolume(canal),
    configurar: (c) => R.SetSettings(c.master, c.music, c.sfx, c.ambience, c.voice, c.mouse, !!c.invertY),
  };
}
