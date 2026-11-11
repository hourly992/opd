$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$editors = 'C:\Program Files\Unity\Hub\Editor'
$unity = Get-ChildItem -LiteralPath $editors -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^6000\.' } |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $unity) {
    Write-Host 'Unity 6 was not found in C:\Program Files\Unity\Hub\Editor.'
    Write-Host 'Install Unity 6 using Unity Hub, or build the project from an existing Unity installation.'
    exit 1
}

$log = Join-Path $project 'build.log'
Write-Host 'Building the Windows game. This can take several minutes...'
$build = Start-Process -FilePath $unity -ArgumentList @('-batchmode','-quit','-accept-apiupdate','-projectPath',('"' + $project + '"'),'-buildTarget','StandaloneWindows64','-executeMethod','StreetBuilder.Build','-logFile',('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
if ($build.ExitCode -ne 0) {
    Write-Host "Build failed. See log: $log"
    exit $build.ExitCode
}
$game = Join-Path $project 'Build\StreetWindows\DuninaGorkavicha.exe'
if (-not (Test-Path -LiteralPath $game)) {
    Write-Host "The game executable was not created. See log: $log"
    exit 1
}
Write-Host "Starting: $game"
Start-Process -FilePath $game -WorkingDirectory (Split-Path $game)
