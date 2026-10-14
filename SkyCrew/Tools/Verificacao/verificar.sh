#!/usr/bin/env bash
# Verificacoes que rodam SEM o Editor da Unity:
#  1) testes EditMode (regras do jogo) com dotnet test
#  2) compilacao do codigo Unity (Runtime, Editor, testes PlayMode) contra DLLs de referencia
# Requisitos: .NET SDK 8 e acesso ao nuget.org.
set -euo pipefail
cd "$(dirname "$0")"
./preparar_referencias.sh
echo "== Testes EditMode (dominio)"
dotnet test Tests/SkyCrew.Tests.EditMode.csproj -nologo
for p in SkyCrew.Runtime SkyCrew.Editor SkyCrew.Tests.PlayMode; do
  echo "== Compilacao: $p"
  dotnet build "UnityCompile/$p/$p.csproj" -nologo -v q
done
echo "Tudo verificado."
