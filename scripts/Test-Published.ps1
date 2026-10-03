param([string]$Package = "$PSScriptRoot\..\artifacts\GameLauncher-5.2-candidate")
$ErrorActionPreference = 'Stop'
$fixture = (Resolve-Path "$PSScriptRoot\..\GameLauncher.TestGame\bin\Release\net10.0-windows\GameLauncher.TestGame.exe").Path
$executable = (Resolve-Path "$Package\GameLauncher.exe").Path
$receipts = @()
foreach ($run in 1..2) {
    $testRoot = Join-Path $env:TEMP ('GameLauncher.52.Start-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$testRoot\GamesData" -Force | Out-Null
    @{
        ArchivePath=$testRoot; ManagedGameFolder="$testRoot\Library"; IsFirstRun=$false
        KeyboardShortcutEnabled=$false; GlobalMouseShortcutEnabled=$false; AutoDiscoveryEnabled=$false
        F95UpdateChecksEnabled=$false; AutoDownloadF95Gallery=$false; AutoAddDetectedGames=$false
        StartWithWindows=$false; LaunchShortcutEnabled=$false; RemoteHostAutoStart=$false
        ReduceMotion=$true; LowEnergyWhilePlaying=$true; RestoreAfterGame=$true
    } | ConvertTo-Json | Set-Content "$testRoot\GamesData\settings.json"
    $process = Start-Process -FilePath $executable -ArgumentList @('--verify-startup','--data-root',('"'+$testRoot+'"'),'--test-game',('"'+$fixture+'"')) -PassThru -WindowStyle Hidden
    if (!$process.WaitForExit(55000)) { throw "Smoke timeout. Isolated process ID: $($process.Id), data: $testRoot" }
    $receiptFile = "$testRoot\smoke-result.json"
    if (!(Test-Path $receiptFile)) { throw "No smoke receipt. Exit=$($process.ExitCode), data=$testRoot" }
    $receipt = Get-Content $receiptFile -Raw | ConvertFrom-Json
    $receipts += [pscustomobject]@{run=$run;root=$testRoot;exitCode=$process.ExitCode;result=$receipt}
    $receipts | ConvertTo-Json -Depth 12 | Set-Content "$PSScriptRoot\..\evidence\published-smoke.json"
    if ($process.ExitCode -ne 0 -or !$receipt.passed) { throw "Smoke failed: $($receipt.error)" }
    Write-Output "PASS published EXE run $run : $testRoot"
}
