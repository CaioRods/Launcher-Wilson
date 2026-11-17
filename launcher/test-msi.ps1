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
    $registry = if ($Name -eq 'Excel AW') { 'ExcelAW' } else { 'WordCraft' }
    $exe = if ($Name -eq 'Excel AW') { 'gridcraft.exe' } else { 'wordcraft.exe' }
    $installed = Get-ItemPropertyValue -LiteralPath "$root\SOFTWARE\$registry" -Name InstallDir
    if (!(Test-Path -LiteralPath (Join-Path $installed $exe) -PathType Leaf)) {
      throw "Arquivo executável de $Name ausente no escopo $root."
    }
    $actual = (Get-Item -LiteralPath (Join-Path $installed $exe)).VersionInfo.ProductName
    if ($actual -ne $Name) { throw "Nome de produto inesperado: $actual" }
    Write-Output "Instalação validada: $Name ($root)"
  } finally {
    Invoke-Msi '/x' $properties
  }
}
