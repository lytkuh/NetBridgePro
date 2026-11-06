# NetBridge Pro — manual adb helper (PowerShell)
# Use ONLY when the in-app wizard is unavailable.
# This talks to the PRIVATE adb server (port 5038), never the system one.

$ErrorActionPreference = "Stop"
$pkg   = "pro.netbridge.android"
$port  = 5038
$env:ANDROID_ADB_SERVER_PORT = "$port"

# locate adb (bundled platform-tools first)
$adb = @(
    "$env:ProgramData\NetBridge\platform-tools\platform-tools\adb.exe",
    "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
    "adb"
) | Where-Object { (Get-Command $_ -ErrorAction SilentlyContinue) } | Select-Object -First 1

if (-not $adb) { Write-Host "adb.exe not found - install platform-tools first"; exit 1 }

Write-Host "== starting private adb server on :$port =="
& $adb -P $port start-server
& $adb -P $port devices

$serial = (& $adb -P $port devices | Select-String -Pattern "`tdevice`b").ToString().Split("`t")[0]
if (-not $serial) { Write-Host "no authorized device"; exit 1 }
Write-Host "== device: $serial =="

foreach ($perm in @(
    "android.permission.WRITE_SECURE_SETTINGS",
    "android.permission.READ_PHONE_STATE",
    "android.permission.ACCESS_FINE_LOCATION",
    "android.permission.POST_NOTIFICATIONS"
)) {
    & $adb -P $port -s $serial shell pm grant $pkg $perm
    Write-Host "granted $perm"
}

& $adb -P $port -s $serial shell settings put global adb_wifi_enabled 0
Write-Host "wireless debugging disabled"
Write-Host "Done. Optional lockdown (turn OFF global adb):"
Write-Host "  & $adb -P $port -s $serial shell settings put global adb_enabled 0"
