param([Parameter(Mandatory)] [string] $Packages)
$ErrorActionPreference = 'Stop'
$installed = @()
try {
  foreach ($app in @('Excel','Word')) {
    $msi = (Resolve-Path (Join-Path $Packages "$app-AW-x64.msi")).Path
    Write-Output "Preparando integração: $app AW"
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart ALLUSERS=2 MSIINSTALLPERUSER=1" -Wait -PassThru
    if ($p.ExitCode -notin @(0,3010)) { throw "Falha ao preparar $app AW: $($p.ExitCode)" }
    $installed += $msi
  }
  Write-Output "Iniciando teste com editores reais"
  & "$PSScriptRoot\test-workspace.ps1" -Real
} finally {
  foreach ($msi in $installed) {
    $p = Start-Process msiexec.exe -ArgumentList "/x `"$msi`" /qn /norestart" -Wait -PassThru
    if ($p.ExitCode -notin @(0,3010)) { Write-Warning "Falha ao remover instalador de teste: $($p.ExitCode)" }
  }
}
