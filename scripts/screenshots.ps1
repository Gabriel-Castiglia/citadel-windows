#
#  screenshots.ps1
#  Citadel: local AI chat for Windows.
#
#  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
#  Date: November 9, 2026
#  SPDX-License-Identifier: GPL-2.0-only
#
#  This program is free software: you can redistribute it and/or modify it under the terms of
#  the GNU General Public License, version 2, as published by the Free Software Foundation.
#  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
#

# Renders the main window, a search, the welcome screen, the memory preview, Settings (Memories and Appearance
# tabs), About and two pages of the setup window, in English, Spanish and Japanese, to docs\images\ for the
# manuals. It uses the --screenshot option of each program: the windows are drawn to files and the programs exit
# by themselves.
#
# -Demo points to a folder with "en", "es" and "ja" subfolders, each holding "conversations" and "memories",
# which are shown in the images. They are copied to a temporary folder that Citadel uses through --data-folder,
# and that folder is deleted afterwards: %APPDATA%\Citadel, where the real conversations live, is never read or
# touched; the images still show the paths as %APPDATA%\Citadel, as in a real installation. Ollama must be
# running (the memory preview asks the model for a real draft). Requires the .NET 10 SDK.

param([Parameter(Mandatory)][string]$Demo)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path ([IO.Path]::GetTempPath()) "citadel-screenshots-$PID"
$app = Join-Path $root 'build\screenshots\app'
$setup = Join-Path $root 'build\screenshots\setup'
dotnet build (Join-Path $root 'src\Citadel\Citadel.csproj') -c Release -o $app
if ($LASTEXITCODE) { throw 'Citadel build failed.' }
dotnet build (Join-Path $root 'installer\SetupUI\CitadelSetup.csproj') -c Release -o $setup
if ($LASTEXITCODE) { throw 'Setup window build failed.' }
$images = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $images | Out-Null

function Render([string]$exe, [string]$png, [string[]]$arguments) {
    $p = Start-Process $exe -PassThru -ArgumentList (@('--screenshot', "`"$png`"") + $arguments)
    if (-not $p.WaitForExit(400000)) { $p.Kill(); throw "Timed out: $png" }
    if (-not (Test-Path $png)) { throw "No image: $png" }
    Write-Host "Image: docs\images\$(Split-Path $png -Leaf)"
}

try {
    foreach ($language in 'en', 'es', 'ja') {
        if (Test-Path $data) { Remove-Item $data -Recurse -Force }
        New-Item -ItemType Directory -Force (Join-Path $data 'conversations'), (Join-Path $data 'memories') | Out-Null
        Copy-Item (Join-Path $Demo "$language\conversations\*.json") (Join-Path $data 'conversations')
        Copy-Item (Join-Path $Demo "$language\memories\*") (Join-Path $data 'memories')
        $exe = Join-Path $app 'Citadel.exe'
        $common = @('--data-folder', "`"$data`"", '--language', $language)
        foreach ($window in 'main', 'settings', 'about', 'save') {
            $name = if ($window -eq 'main') { 'chat' } else { $window }
            Render $exe (Join-Path $images "$name-$language.png") (@('--window', $window) + $common)
        }
        # The Appearance tab of Settings (tab 3).
        Render $exe (Join-Path $images "appearance-$language.png") (@('--window', 'settings', '--tab', '3') + $common)
        # A search in the text of the demonstration conversations.
        $query = @{ en = 'list'; es = 'lista'; ja = 'リスト' }[$language]
        Render $exe (Join-Path $images "search-$language.png") (@('--window', 'main', '--search', $query) + $common)
        Remove-Item (Join-Path $data 'conversations\*.json')
        Render $exe (Join-Path $images "welcome-$language.png") (@('--window', 'main') + $common)
        foreach ($page in 'install', 'uninstall') {
            Render (Join-Path $setup 'CitadelSetup.exe') (Join-Path $images "setup-$page-$language.png") @('--page', $page, '--language', $language)
        }
    }
}
finally {
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
}
