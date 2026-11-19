$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path ([System.IO.Path]::GetTempPath()) ("OLEDSaver-Tests-" + [guid]::NewGuid().ToString('N') + '.exe')
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot '..\*.cs') | ForEach-Object { $_.FullName })
try {
    & $compiler /nologo /target:exe /main:SettingsTests "/out:$testExe" @sources (Join-Path $PSScriptRoot 'SettingsTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Settings tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
}
