#!/bin/bash
HERE="$(pwd)"

echo "[*] Cleaning up old instances..."
sudo pkill -f Daemon 2>/dev/null
sudo pkill -f UI 2>/dev/null
sudo rm -f /run/warp-gacha.sock

echo "[*] Granting execution permissions..."
chmod +x "$HERE/AppDir/usr/bin/daemon/Daemon"
chmod +x "$HERE/AppDir/usr/bin/ui/UI"

echo "[*] Starting Daemon under root/pkexec..."
if [ "$(id -u)" -ne 0 ]; then
    pkexec "$HERE/AppDir/usr/bin/daemon/Daemon" &
else
    "$HERE/AppDir/usr/bin/daemon/Daemon" &
fi

echo "[*] Waiting for daemon socket..."
COUNT=0
while [ ! -S /run/warp-gacha.sock ] && [ $COUNT -lt 10 ]; do
    sleep 0.5
    COUNT=$((COUNT + 1))
done

if [ -S /run/warp-gacha.sock ]; then
    echo "[+] Daemon socket ready! Launching UI..."
    "$HERE/AppDir/usr/bin/ui/UI"
else
    echo "[!] Failed to detect daemon socket. Check pkexec permissions."
fi
