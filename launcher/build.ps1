param([Parameter(Mandatory)] [string] $Destination)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Destination).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $root 'Alimentos Wilson.exe'
$flags = @('/nologo', '/target:winexe', '/platform:x64', '/codepage:65001',
  '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
  "/out:$exe", "/win32icon:$PSScriptRoot\assets\wilson.ico", "/resource:$PSScriptRoot\assets\wilson.png,wilson",
  "/resource:$PSScriptRoot\assets\excel.png,excel", "/resource:$PSScriptRoot\assets\word.png,word",
  "$PSScriptRoot\Program.cs", "$PSScriptRoot\Distribution.cs")
& $compiler @flags
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a Central de Apps.' }

$smoke = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -Wait -PassThru
if ($smoke.ExitCode -ne 0) { throw "A Central de Apps falhou ao inicializar ($($smoke.ExitCode))." }
Copy-Item -LiteralPath "$PSScriptRoot\LEIA-ME.txt" -Destination $root

$tests = Join-Path $env:TEMP ('Wilson-distribution-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$tests" "$PSScriptRoot/Distribution.cs" "$PSScriptRoot/tests/DistributionTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar os testes de distribuição.' }
    & $tests
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de distribuição.' }
} finally {
    Remove-Item -LiteralPath $tests -ErrorAction SilentlyContinue
}
