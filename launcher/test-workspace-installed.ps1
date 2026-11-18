param([Parameter(Mandatory)] [string] $Packages)
$ErrorActionPreference = 'Stop'
$installed = @()
$passed = $false
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
  $passed = $true
} finally {
  # Failed tests may leave a hung native window. Do not block diagnostic uploads
  # on Restart Manager in a disposable Actions VM; the VM is destroyed afterwards.
  foreach ($msi in $(if ($passed) { $installed } else { @() })) {
    $p = Start-Process msiexec.exe -ArgumentList "/x `"$msi`" /qn /norestart" -Wait -PassThru
    if ($p.ExitCode -notin @(0,3010)) { Write-Warning "Falha ao remover instalador de teste: $($p.ExitCode)" }
  }
}
