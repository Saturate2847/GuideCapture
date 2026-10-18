$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$source = Join-Path $PSScriptRoot 'GuideCapture.cs'
$output = Join-Path $PSScriptRoot 'GuideCapture.exe'
& $compiler /nologo /target:winexe "/out:$output" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" $source
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built $output"
