$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\publish\windows-iis'))
$target = '\\Gimkim-nas\c\Users\tatsa\web\imagegallery'
$data = '\\Gimkim-nas\c\Users\tatsa\web-data'
if (!(Test-Path -LiteralPath "$source\WebGallery.dll") -or !(Test-Path -LiteralPath "$target\web.config")) { throw 'Expected publish/target not found.' }
if (Test-Path -LiteralPath "$target\app_offline.htm") { throw 'Existing maintenance file; refusing to interfere.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $data "backups\$stamp-index-deploy"
New-Item -ItemType Directory -Path "$backup\app","$backup\state" | Out-Null
Get-ChildItem -LiteralPath $target | Copy-Item -Destination "$backup\app" -Recurse
$configHashes = @{}
foreach ($name in @('appsettings.json','web.config')) { $configHashes[$name] = (Get-FileHash -LiteralPath "$target\$name").Hash }
$maintenanceOwned = $false
$replaced = [Collections.Generic.List[string]]::new()
try {
    Copy-Item -LiteralPath "$PSScriptRoot\maintenance.html" -Destination "$target\app_offline.htm"
    $maintenanceOwned = $true
    $unlocked = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        try {
            $handle = [IO.File]::Open("$data\gallery.db", [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $handle.Dispose(); $unlocked = $true; break
        } catch [IO.IOException] { }
    }
    if (!$unlocked) { throw 'Database still open; aborting before replacement.' }
    foreach ($name in @('gallery.db','gallery.db-wal','gallery.db-shm','keys')) {
        if (Test-Path -LiteralPath "$data\$name") { Copy-Item -LiteralPath "$data\$name" -Destination "$backup\state" -Recurse }
    }
    $published = Get-ChildItem -LiteralPath $source -Recurse -File
    foreach ($file in $published) {
        $relative = $file.FullName.Substring($source.Length + 1)
        if ($relative -in @('appsettings.json','web.config')) { continue }
        $destination = Join-Path $target $relative
        if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) { continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        for ($attempt = 0; ; $attempt++) {
            try { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force; break }
            catch [IO.IOException] { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 500 }
        }
        $replaced.Add($relative)
    }
    foreach ($file in $published) {
        $relative = $file.FullName.Substring($source.Length + 1)
        if ($relative -in @('appsettings.json','web.config')) { continue }
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash) { throw "Hash mismatch: $relative" }
    }
    foreach ($name in $configHashes.Keys) {
        if ((Get-FileHash -LiteralPath "$target\$name").Hash -ne $configHashes[$name]) { throw "Configuration changed: $name" }
    }
    Write-Output "Copied and hash-verified application. Config preserved. Backup: $backup"
} catch {
    foreach ($relative in $replaced) {
        if (Test-Path -LiteralPath "$backup\app\$relative") { Copy-Item -LiteralPath "$backup\app\$relative" -Destination (Join-Path $target $relative) -Force }
    }
    throw
} finally {
    if ($maintenanceOwned) { Remove-Item -LiteralPath "$target\app_offline.htm" }
}
