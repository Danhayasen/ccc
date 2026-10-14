// Testes da versao de navegador no Chromium headless (Playwright).
// Uso:
//   1) dotnet publish SkyCrew/Web/Regras -c Release -o /tmp/regras
//      e copie /tmp/regras/wwwroot/_framework para SkyCrew/Web/jogo/regras/
//   2) sirva SkyCrew/Web/jogo:   npx http-server SkyCrew/Web/jogo -p 8124
//   3) node testar.mjs [partida|eventos]
// Sem acesso ao CDN, defina THREE_LOCAL com a pasta do pacote "three@0.170.0" (npm pack three@0.170.0).
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = path.dirname(fileURLToPath(import.meta.url));
const roteiro = process.argv[2] ?? 'partida';
const url = process.env.URL ?? 'http://127.0.0.1:8124/index.html';
const CDN = 'https://cdn.jsdelivr.net/npm/three@0.170.0/';

const navegador = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const pagina = await navegador.newPage({ viewport: { width: 1280, height: 720 } });
if (process.env.THREE_LOCAL) {
  await pagina.route(CDN + '**', r => r.fulfill({
    path: path.join(process.env.THREE_LOCAL, r.request().url().slice(CDN.length)), contentType: 'application/javascript' }));
}
const erros = [];
pagina.on('pageerror', e => erros.push('ERRO NA PAGINA: ' + e.message));
pagina.on('console', m => { if (m.type() === 'error') erros.push('console: ' + m.text()); });

await pagina.goto(url);
await pagina.waitForFunction(() => window.SKYCREW, null, { timeout: 120000 });
await pagina.addScriptTag({ path: path.join(aqui, 'robo.js') });
await pagina.evaluate(() => bot.iniciar());
console.log(await pagina.evaluate(() => bot.testarTeclado()));

let ok;
if (roteiro === 'eventos') {
  console.log(await pagina.evaluate(() => botEventos()));
  ok = await pagina.evaluate(() => !SKYCREW.estado.tasks.some(t => /liberar_corredor|consertar|limpar/.test(t.id) && t.state === 'Failed'));
} else {
  let n = 0;
  while (await pagina.evaluate(() => bot.rodada()) && n++ < 120) { /* joga ate o pouso */ }
  const resumo = JSON.parse(await pagina.evaluate(() => bot.resumo()));
  console.log(JSON.stringify(resumo.resultado));
  ok = resumo.resultado?.victory === true;
}
await pagina.screenshot({ path: path.join(aqui, 'ultima_execucao.png') });
await navegador.close();
if (erros.length) console.log(erros.join('\n'));
console.log(ok && erros.length === 0 ? 'OK' : 'FALHOU');
process.exit(ok && erros.length === 0 ? 0 : 1);
