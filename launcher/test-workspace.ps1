param([switch] $Real)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $env:RUNNER_TEMP ('AWcontrol-workspace-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
  & $compiler /nologo /codepage:65001 /target:exe /platform:x64 /main:WorkspaceTests /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\workspace.manifest" "/resource:$PSScriptRoot\assets\wilson.png,wilson" "/resource:$PSScriptRoot\assets\excel.png,excel" "/resource:$PSScriptRoot\assets\word.png,word" "/out:$test" "$PSScriptRoot\Program.cs" "$PSScriptRoot\Workspace.cs" "$PSScriptRoot\Distribution.cs" "$PSScriptRoot\tests\WorkspaceTests.cs"
  if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar os testes do AWcontrol.' }
  if ($Real) { & $test --real } else { & $test }
  if ($LASTEXITCODE -ne 0) { throw 'Falha na integração de páginas do AWcontrol.' }
} finally { Remove-Item -LiteralPath $test -ErrorAction SilentlyContinue }
