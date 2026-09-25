#!/usr/bin/env bash
# Records the control panel tour and takes the README screenshots, in one language.
#   bash MouseGlowTrail.Bench/record-panel.sh English|Chinese <outdir>
# Runs the isolated dev copy with a solid background and an always-playing preview; frames come from
# PrintWindow, so nothing but the panel itself is captured.
set -e
language="${1:-English}"
out="${2:-MouseGlowTrail.Bench/out/panel-$language}"
cd "$(dirname "$0")/.."
drive() { powershell -NoProfile -ExecutionPolicy Bypass -File MouseGlowTrail.Bench/panel-drive.ps1 -Steps "$1"; }
start() {
  mkdir -p "$TEMP/MouseGlowTrail-dev"
  printf '{"Version":3,"Language":"%s","Sparkles":true,"WelcomeShown":true,"Opacity":80}' "$language" \
    > "$TEMP/MouseGlowTrail-dev/settings.json"
  MGT_NO_MICA=1 MGT_ALWAYS_PREVIEW=1 MGT_THEME="$1" bash MouseGlowTrail.Bench/dev.sh >/dev/null
}
rm -rf "$out" && mkdir -p "$out"

start light
drive "size 1080 780; move 700 300; wait 1200; rec $out/tour 15; wait 2500; click 647 515; wait 1500; click 779 515; wait 1500; click 383 515; wait 900; click 100 110; wait 1300; click 709 540; wait 1900; click 933 540; wait 1900; click 100 150; wait 900; click 781 441; wait 800; click 781 512; wait 3600; click 100 190; wait 2600; stop"
drive "click 100 70; wait 1500; grab $out/light-appearance.png; click 100 110; wait 1500; grab $out/light-particles.png; click 100 150; wait 2200; grab $out/light-clicks.png"

start dark
drive "size 1080 780; move 700 300; wait 1500; grab $out/dark-appearance.png; click 100 190; wait 1500; grab $out/dark-pointer.png"
taskkill //PID "$(tr -d '\r\n' < MouseGlowTrail.Bench/dev.pid)" //F >/dev/null 2>&1 || true
rm -f MouseGlowTrail.Bench/dev.pid
echo "done: $out"
