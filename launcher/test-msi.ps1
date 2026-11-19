param([string] $Package, [string] $Name)
$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $Package).Path

function Invoke-Msi([string] $Verb, [string] $Properties) {
  $log = Join-Path $env:RUNNER_TEMP ([guid]::NewGuid().ToString() + '.log')
  $p = Start-Process -FilePath "$env:WINDIR\System32\msiexec.exe" -ArgumentList "$Verb ""$msi"" /qn /norestart /L*v ""$log"" $Properties" -Wait -PassThru
  if ($p.ExitCode -notin @(0, 3010)) {
    Get-Content -LiteralPath $log -Tail 85
    throw "Windows Installer retornou $($p.ExitCode) ($Name, $Verb $Properties)"
  }
}

foreach ($machine in @($false, $true)) {
  $properties = if ($machine) { 'ALLUSERS=1' } else { 'ALLUSERS=2 MSIINSTALLPERUSER=1' }
  $root = if ($machine) { 'HKLM:' } else { 'HKCU:' }
  Invoke-Msi '/i' $properties
  try {
    $exe = if ($Name -eq 'Excel AW') { 'gridcraft.exe' } else { 'wordcraft.exe' }
    $app = if ($Name -eq 'Excel AW') { 0 } else { 1 }
    $installed = (Get-Item -LiteralPath "$root\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$exe").GetValue('')
    if (!(Test-Path -LiteralPath $installed -PathType Leaf)) {
      throw "Arquivo executável de $Name ausente no escopo $root."
    }
    $actual = (Get-Item -LiteralPath $installed).VersionInfo.ProductName
    if ($actual -ne $Name) { throw "Nome de produto inesperado: $actual" }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $tests = Join-Path $env:RUNNER_TEMP ('Wilson-installed-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
    try {
      & $compiler /nologo /codepage:65001 /target:exe /reference:System.Web.Extensions.dll "/out:$tests" "$PSScriptRoot\Distribution.cs" "$PSScriptRoot\tests\DistributionTests.cs"
      if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a validação do launcher.' }
      & $tests $app $installed
      if ($LASTEXITCODE -ne 0) { throw 'O launcher não reconheceu o aplicativo instalado.' }
      $process = Start-Process -FilePath $installed -ArgumentList '--version' -PassThru -Wait
      if ($process.ExitCode -ne 0) { throw 'O aplicativo instalado falhou ao consultar sua versão.' }
    } finally { Remove-Item -LiteralPath $tests -ErrorAction SilentlyContinue }
    Write-Output "Instalação validada: $Name ($root)"
  } finally {
    Invoke-Msi '/x' $properties
  }
}
