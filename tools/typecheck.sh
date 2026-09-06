#!/usr/bin/env bash
#
# Compile the project's C# without opening Unity.
#
# Unity ships Roslyn and a .NET runtime inside the editor install, so a full type check costs a
# few seconds from a terminal. Worth having because the alternative - alt-tabbing to the editor,
# waiting for a domain reload, and reading the console - is slow enough that people stop doing it,
# and this project has repeatedly shipped changes that had never once been compiled.
#
# It builds the two assemblies Unity builds, in the same order:
#   Assembly-CSharp         everything under Assets/Scripts except Editor/
#   Assembly-CSharp-Editor  Assets/Scripts/Editor, referencing the first
#
# Output goes to Temp/ and is thrown away. Nothing here touches the project.
#
#   ./tools/typecheck.sh
#
# Requires the Unity editor to be installed (not just the Hub) and the project to have been
# opened at least once, so Library/ScriptAssemblies exists for the package references.

set -uo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"

# ---------------------------------------------------------------- find the editor

HUB="/mnt/c/Program Files/Unity/Hub/Editor"
VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
DATA="$HUB/$VERSION/Editor/Data"

if [ ! -d "$DATA" ]; then
    echo "No Unity $VERSION at $DATA"
    echo "Installed versions:"; ls "$HUB" 2>/dev/null | sed 's/^/  /'
    exit 1
fi

CSC="$DATA/DotNetSdkRoslyn/csc.dll"
DOTNET="$DATA/NetCoreRuntime/dotnet.exe"

if [ ! -f "$DOTNET" ] || [ ! -f "$CSC" ]; then
    echo "Unity $VERSION has no bundled Roslyn or .NET runtime."
    exit 1
fi

if [ ! -d Library/ScriptAssemblies ]; then
    echo "No Library/ScriptAssemblies - open the project in Unity once first."
    exit 1
fi

mkdir -p Temp

# ---------------------------------------------------------------- references
#
# UnityEditor.dll is deliberately excluded: it is a facade that forwards to the
# UnityEditor.*Module assemblies, so referencing both makes every MenuItem and SerializedObject
# ambiguous. Unity's own build references the modules.

python3 - "$DATA" "$ROOT" <<'PY'
import glob, os, sys
data, root = sys.argv[1], sys.argv[2]

def win(p):
    p = os.path.abspath(p)
    return "C:\\" + p[7:].replace("/", "\\") if p.startswith("/mnt/c/") else p

refs  = [p for p in glob.glob(data + "/Managed/UnityEngine/*.dll")
         if not p.endswith("/Managed/UnityEngine/UnityEditor.dll")]
refs += [p for p in glob.glob(root + "/Library/ScriptAssemblies/*.dll")
         if "Assembly-CSharp" not in os.path.basename(p)]
# Managed assemblies only. Packages ship native binaries too (steam_api64.dll and friends)
# and Roslyn rejects them with "PE image doesn't contain managed metadata".
refs += [p for p in glob.glob(root + "/Packages/**/*.dll", recursive=True)
         if "redistributable_bin" not in p and "/Plugins/" not in p]
refs += [data + "/NetStandard/ref/2.1.0/netstandard.dll",
         data + "/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll"]

# Facepunch ships one DLL per platform and they define the same types. Win64 only.
refs = [r for r in refs if "Facepunch.Steamworks." not in r or "Win64" in r]

common = ["/target:library", "/nostdlib+", "/noconfig", "/langversion:9.0",
          "/nowarn:CS0169,CS0414,CS0649"]

runtime = [f for f in glob.glob(root + "/Assets/Scripts/**/*.cs", recursive=True)
           if "/Editor/" not in f]
editor  = glob.glob(root + "/Assets/Scripts/Editor/**/*.cs", recursive=True)

def write(name, sources, extra_refs):
    lines  = [f'/r:"{win(r)}"' for r in refs + extra_refs]
    lines += common + [f'/out:"{win(root)}\\Temp\\{name}.dll"']
    lines += [f'"{win(s)}"' for s in sources]
    open(f"{root}/Temp/{name}.rsp", "w").write("\n".join(lines))
    return len(sources)

n1 = write("typecheck-runtime", runtime, [])
n2 = write("typecheck-editor",  editor,  [root + "/Temp/typecheck-runtime.dll"])
print(f"{n1} runtime sources, {n2} editor sources, {len(refs)} references")
PY

# ---------------------------------------------------------------- compile

WROOT="C:\\${ROOT#/mnt/c/}"; WROOT="${WROOT//\//\\}"
WCSC="C:\\${CSC#/mnt/c/}";   WCSC="${WCSC//\//\\}"

fail=0
for stage in runtime editor; do
    printf '%-8s ' "$stage"
    out="$("$DOTNET" "$WCSC" "@$WROOT\\Temp\\typecheck-$stage.rsp" 2>&1)"
    errors="$(printf '%s' "$out" | grep -c 'error CS')"

    if [ "$errors" -eq 0 ]; then
        echo "ok"
    else
        echo "$errors errors"
        printf '%s\n' "$out" | grep 'error CS' | sort -u | head -40 | sed 's/^/    /'
        fail=1

        # The editor assembly references the runtime one, so a broken runtime build makes the
        # editor errors meaningless noise. Say so rather than printing both.
        [ "$stage" = runtime ] && { echo "    (skipping editor - it references this)"; break; }
    fi
done

exit $fail
