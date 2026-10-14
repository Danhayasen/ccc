#!/usr/bin/env bash
# Baixa do NuGet as DLLs de REFERENCIA da Unity usadas so para checar a compilacao
# fora do Editor (nao sao redistribuidas no repositorio):
#   - UnityEngine.Modules 2021.3.33 (API do motor; a Unity 6 e um superconjunto quase total)
#   - Unity3D.SDK 2021.1.14.1 (somente UnityEditor.dll)
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p refs/UnityEngine refs/UnityEditor refs/_dl
if [ ! -f refs/UnityEngine/UnityEngine.CoreModule.dll ]; then
  curl -sSL -o refs/_dl/mods.nupkg https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
  python3 - <<'PY'
import zipfile, os
z = zipfile.ZipFile('refs/_dl/mods.nupkg')
for n in z.namelist():
    if n.startswith('lib/netstandard2.0/') and n.endswith('.dll'):
        open('refs/UnityEngine/' + os.path.basename(n), 'wb').write(z.read(n))
PY
fi
if [ ! -f refs/UnityEditor/UnityEditor.dll ]; then
  curl -sSL -o refs/_dl/sdk.nupkg https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg
  python3 - <<'PY'
import zipfile
z = zipfile.ZipFile('refs/_dl/sdk.nupkg')
open('refs/UnityEditor/UnityEditor.dll', 'wb').write(z.read('lib/UnityEditor.dll'))
PY
fi
rm -rf refs/_dl
echo "Referencias prontas em $(pwd)/refs"
