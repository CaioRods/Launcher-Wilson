param([string] $Source, [ValidateSet('Excel','Word')] [string] $App)
$ErrorActionPreference = 'Stop'

$package = if ($App -eq 'Excel') { 'gridcraft' } else { 'wordcraft' }
$old = if ($App -eq 'Excel') { 'ExcelAW' } else { 'WordCraft' }
$name = "$App AW"
$packaging = Join-Path $Source 'packaging/windows'
$sourceFile = Join-Path $packaging "$package.wxs"
$content = Get-Content -LiteralPath $sourceFile -Raw
$content = $content.Replace("<Package Name=""$old""", "<Package Name=""$name""")
$content = $content.Replace("Title=""$old""", "Title=""$name""")
$content = $content.Replace("Directory=""ProgramMenuFolder"" Name=""$old""", "Directory=""ProgramMenuFolder"" Name=""$name""")
$content = $content.Replace("Id=""DesktopShortcut"" Name=""$old""", "Id=""DesktopShortcut"" Name=""$name""")
$content = $content.Replace('Learning Machines LLC', 'Caio Victor Rodrigues')
$content = $content.Replace('WordCraft word processor', 'Editor de documentos Word AW')
$content = $content.Replace('A newer version of WordCraft is already installed.', 'Uma versão mais recente do Word AW já está instalada.')
$content = $content.Replace('Launch WordCraft', 'Abrir Word AW')
$content = $content.Replace('Planilhas ExcelAW', 'Planilhas Excel AW')
$content = $content.Replace('Instalação do ExcelAW', 'Instalação do Excel AW')
$content = $content.Replace("Name=""ApplicationName"" Value=""$old""", "Name=""ApplicationName"" Value=""$name""")
$content = $content.Replace("Name=""FriendlyAppName"" Value=""$old""", "Name=""FriendlyAppName"" Value=""$name""")
Set-Content -LiteralPath $sourceFile -Value $content -Encoding utf8

$policyFile = Join-Path $packaging 'install-policy.json'
$policy = Get-Content -LiteralPath $policyFile -Raw | ConvertFrom-Json
foreach ($entry in $policy.registry) {
  if ($entry[2] -in @('ApplicationName','FriendlyAppName') -and $entry[3] -eq $old) {
    $entry[3] = $name
  }
}
$policy | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $policyFile -Encoding utf8
Copy-Item -LiteralPath "$PSScriptRoot/assets/$($App.ToLowerInvariant()).ico" -Destination (Join-Path $Source "assets/app-icon/$package.ico") -Force

$resource = Join-Path $Source "apps/$package/build.rs"
$content = (Get-Content -LiteralPath $resource -Raw).Replace("""ProductName"", ""$old""", """ProductName"", ""$name""")
$content = $content.Replace('WordCraft word processor', 'Editor de documentos Word AW')
$content = $content.Replace('Planilhas ExcelAW', 'Planilhas Excel AW')
$content = $content.Replace('Learning Machines LLC', 'Caio Victor Rodrigues')
Set-Content -LiteralPath $resource -Value $content -Encoding utf8
