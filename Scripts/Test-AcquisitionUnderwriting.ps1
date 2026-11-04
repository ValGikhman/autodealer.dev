param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $repoRoot 'bin/roslyn/csc.exe'
$jsonAssembly = Join-Path $repoRoot 'bin/Newtonsoft.Json.dll'
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $jsonAssembly)) { throw 'Restore/build the application dependencies first (bin/roslyn/csc.exe and bin/Newtonsoft.Json.dll).' }
$checkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('AutoDealerUnderwriting-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($checkDirectory)
$checkExe = Join-Path $checkDirectory 'AcquisitionUnderwritingRegression.exe'
$checkJson = Join-Path $checkDirectory 'Newtonsoft.Json.dll'
try {
    Copy-Item -LiteralPath $jsonAssembly -Destination $checkJson
    $compilerArguments = @('/nologo', '/target:exe', ('/out:' + $checkExe), ('/r:' + $jsonAssembly), '/r:System.Xml.Linq.dll', '/r:System.Web.dll')
    $compilerArguments += @('Services/AcquisitionMarketPrice.cs', 'Services/AcquisitionReportDocument.cs', 'Services/AcquisitionUnderwriting.cs', 'Tests/AcquisitionUnderwritingRegression.cs') | ForEach-Object { Join-Path $repoRoot $_ }
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Underwriting regression checks did not compile.' }
    & $checkExe
    if ($LASTEXITCODE -ne 0) { throw 'Underwriting regression checks failed.' }
} finally {
    foreach ($file in @($checkExe, $checkJson)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } }
    if (Test-Path -LiteralPath $checkDirectory) { Remove-Item -LiteralPath $checkDirectory }
}
