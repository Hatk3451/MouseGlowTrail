#!/usr/bin/env bash
# Rebuilds and restarts the isolated development copy (never the installed one), opening its panel.
set -e
cd "$(dirname "$0")/.."
if [ -f MouseGlowTrail.Bench/dev.pid ]; then
  taskkill //PID "$(cat MouseGlowTrail.Bench/dev.pid)" //F >/dev/null 2>&1 || true
  rm -f MouseGlowTrail.Bench/dev.pid
  sleep 0.5
fi
(cd MouseGlowTrail && dotnet build -c Release 2>&1 | grep -E " error |个错误" | sed 's/\[.*csproj\]//' | sort -u)
powershell -NoProfile -Command "(Start-Process -FilePath 'MouseGlowTrail\bin\Release\net10.0-windows\win-x64\MouseGlowTrail.exe' -ArgumentList '--isolated','--panel' -PassThru).Id" > MouseGlowTrail.Bench/dev.pid
sleep 1.5
echo "dev pid $(cat MouseGlowTrail.Bench/dev.pid)"
