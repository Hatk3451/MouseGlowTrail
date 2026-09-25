#!/usr/bin/env bash
# Memory of the isolated dev copy: before the panel, with it open (preview running), and after closing it.
# EXE overrides the executable (e.g. EXE='publish-test\MouseGlowTrail.exe').
cd "$(dirname "$0")/.."
exe="${EXE:-MouseGlowTrail\\bin\\Release\\net10.0-windows\\win-x64\\MouseGlowTrail.exe}"
if [ -f MouseGlowTrail.Bench/dev.pid ]; then taskkill //PID "$(cat MouseGlowTrail.Bench/dev.pid)" //F >/dev/null 2>&1; rm -f MouseGlowTrail.Bench/dev.pid; sleep 0.5; fi
powershell -NoProfile -Command "(Start-Process -FilePath '$exe' -ArgumentList '--isolated' -PassThru).Id" > MouseGlowTrail.Bench/dev.pid
pid=$(cat MouseGlowTrail.Bench/dev.pid)
echo "exe $exe pid $pid"
measure() {
  powershell -NoProfile -Command "\$p=Get-Process -Id $pid; \$a=\$p.TotalProcessorTime; Start-Sleep 4; \$p.Refresh(); \$b=\$p.TotalProcessorTime; '$1'.PadRight(14) + ' cpu ' + [math]::Round((\$b-\$a).TotalMilliseconds/40,1) + '%  ws ' + [math]::Round(\$p.WorkingSet64/1MB,1) + ' MB  private ' + [math]::Round(\$p.PrivateMemorySize64/1MB,1) + ' MB'"
}
sleep 3
measure "no panel"
# A second launch opens the running copy's panel.
powershell -NoProfile -Command "Start-Process -FilePath '$exe' -ArgumentList '--isolated'"
sleep 2
powershell -NoProfile -ExecutionPolicy Bypass -File MouseGlowTrail.Bench/panel-drive.ps1 -Steps "wait 100; front" >/dev/null
measure "panel open"
powershell -NoProfile -ExecutionPolicy Bypass -File MouseGlowTrail.Bench/panel-drive.ps1 -Steps "key 27" >/dev/null
sleep 3
measure "panel closed"
