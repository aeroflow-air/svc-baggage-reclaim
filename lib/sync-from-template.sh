#!/usr/bin/env bash
# Re-pack AeroFlow.ServiceDefaults from a template-dotnet-service checkout into lib/packages.
# Usage: ./lib/sync-from-template.sh /path/to/template-dotnet-service [commit]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${1:?path to template-dotnet-service checkout}"
pin="${2:-50aac82d82c43b6274eb5bd1dda5fc397ac823fd}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [[ -d "$src/.git" ]]; then
  git -C "$src" fetch --quiet origin 2>/dev/null || true
  git -C "$src" archive "$pin" src/AeroFlow.ServiceDefaults src/AeroFlow.ServiceDefaults.Testing docs/service-defaults.md \
    | tar -x -C "$work"
  cp "$work/docs/service-defaults.md" "$root/docs/service-defaults.md"
else
  cp "$src/docs/service-defaults.md" "$root/docs/service-defaults.md"
  mkdir -p "$work/src"
  rsync -a "$src/src/AeroFlow.ServiceDefaults/" "$work/src/AeroFlow.ServiceDefaults/"
  rsync -a "$src/src/AeroFlow.ServiceDefaults.Testing/" "$work/src/AeroFlow.ServiceDefaults.Testing/"
fi

cat > "$work/Directory.Build.props" <<'EOF'
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
EOF

defaults="$work/src/AeroFlow.ServiceDefaults"
testing="$work/src/AeroFlow.ServiceDefaults.Testing"
out="$root/lib/packages"
mkdir -p "$out"
dotnet pack "$defaults/AeroFlow.ServiceDefaults.csproj" -c Release -o "$out"
dotnet pack "$testing/AeroFlow.ServiceDefaults.Testing.csproj" -c Release -o "$out"
echo "Packed AeroFlow.ServiceDefaults from $src at $pin into $out"
echo "Update lib/README.md if the pin changed."
