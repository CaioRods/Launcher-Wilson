param([switch] $Real)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $env:RUNNER_TEMP ('AWcontrol-workspace-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
$stdout = "$test.stdout.log"
$stderr = "$test.stderr.log"
try {
  & $compiler /nologo /codepage:65001 /target:exe /platform:x64 /main:WorkspaceTests /reference:Accessibility.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\workspace.manifest" "/resource:$PSScriptRoot\assets\wilson.png,wilson" "/resource:$PSScriptRoot\assets\awcontrol.png,awcontrol" "/resource:$PSScriptRoot\assets\awcontrol.ico,awcontrol.ico" "/resource:$PSScriptRoot\assets\excel.png,excel" "/resource:$PSScriptRoot\assets\word.png,word" "/out:$test" "$PSScriptRoot\Program.cs" "$PSScriptRoot\HomeView.cs" "$PSScriptRoot\Workspace.cs" "$PSScriptRoot\EditorControl.cs" "$PSScriptRoot\TemplateCatalog.cs" "$PSScriptRoot\WindowChrome.cs" "$PSScriptRoot\DockingService.cs" "$PSScriptRoot\SelectedHandoff.cs" "$PSScriptRoot\Distribution.cs" "$PSScriptRoot\tests\WorkspaceTests.cs"
  if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar os testes do AWcontrol.' }
  $parameters = @{ FilePath=$test; PassThru=$true; RedirectStandardOutput=$stdout; RedirectStandardError=$stderr }
  if ($Real) { $parameters.ArgumentList = '--real' }
  $process = Start-Process @parameters
  if (!$process.WaitForExit(180000)) {
    taskkill /PID $process.Id /T /F
    throw 'O teste de integração excedeu 180 segundos.'
  }
  if ($process.ExitCode -ne 0) { throw 'Falha na integração de páginas do AWcontrol.' }
} finally {
  if (Test-Path $stdout) { Get-Content $stdout }
  if (Test-Path $stderr) { Get-Content $stderr }
  Remove-Item -LiteralPath $test -ErrorAction SilentlyContinue
}
