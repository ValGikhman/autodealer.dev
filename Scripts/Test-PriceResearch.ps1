param([switch]$BuildLocal,[switch]$Live,[switch]$LiveNeoVin)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('AutoDealerPriceCheck-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$savedProviderKey=$env:MARKETCHECK_API_KEY
try {
 $project=[xml](Get-Content -LiteralPath (Join-Path $repoRoot 'autodealer.dev.csproj') -Raw)
 $ns=[Xml.XmlNamespaceManager]::new($project.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/developer/msbuild/2003')
 $sources=$project.SelectNodes('//p:Compile',$ns)|ForEach-Object{Join-Path $repoRoot $_.GetAttribute('Include')}
 $references=@('/r:System.dll','/r:System.Core.dll','/r:System.Configuration.dll','/r:System.Data.dll','/r:System.Data.Linq.dll','/r:System.ComponentModel.DataAnnotations.dll','/r:System.Net.Http.dll','/r:System.Runtime.Caching.dll','/r:System.Web.dll','/r:System.Web.Extensions.dll','/r:System.Xml.dll','/r:System.Xml.Linq.dll','/r:System.Security.dll','/r:Microsoft.CSharp.dll')
 Get-ChildItem -LiteralPath (Join-Path $repoRoot 'bin') -Filter '*.dll' | Where-Object {$_.Name -ne 'autodealer.dev.dll'} | ForEach-Object {$references+=('/r:'+ $_.FullName);Copy-Item -LiteralPath $_.FullName -Destination $temporary}
 $compiler=Join-Path $repoRoot 'bin\roslyn\csc.exe'
 $assembly=Join-Path $temporary 'autodealer.dev.dll'
 & $compiler /nologo /langversion:7.3 /target:library ('/out:'+$assembly) @references @sources
 if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
 $site=[xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Web.config') -Raw)
 foreach($test in @('PriceResearchRegression','PriceResearchHttpRegression','NeoVinRegression','NeoVinHttpRegression','ResearchMajordomeRegression','AuctionBidRegression','ResearchDefaultApiKeyRegression','MarketDemandRegression','ClientResearchRegression')){
  $exe=Join-Path $temporary ($test+'.exe')
  & $compiler /nologo /langversion:7.3 /target:exe ('/out:'+$exe) @references ('/r:'+$assembly) (Join-Path $repoRoot ('Tests\'+$test+'.cs'))
  if($LASTEXITCODE -ne 0){throw ($test+' compilation failed.')}
  $config=[xml]'<configuration><appSettings><add key="ApiSecurity:Enabled" value="true" /></appSettings></configuration>'
  if($site.configuration.runtime){[void]$config.configuration.AppendChild($config.ImportNode($site.configuration.runtime,$true))}
  $config.Save($exe+'.config')
  if(($Live -and $test -eq 'PriceResearchHttpRegression') -or ($LiveNeoVin -and $test -eq 'NeoVinHttpRegression')){
   $key=$site.configuration.appSettings.add|Where-Object {$_.key -eq 'MarketCheck:ApiKey'}
   if($key){$env:MARKETCHECK_API_KEY=$key.value}
   & $exe --live
  }else{& $exe}
  if($LASTEXITCODE -ne 0){throw ($test+' failed.')}
 }
 if($BuildLocal){Copy-Item -LiteralPath $assembly -Destination (Join-Path $repoRoot 'bin\autodealer.dev.dll') -Force;Write-Output 'Built vehicle research APIs into local AutoDealer.dev.'}
} finally {
 $env:MARKETCHECK_API_KEY=$savedProviderKey
 # Remove only the isolated compiler/test outputs created by this script.
 Get-ChildItem -LiteralPath $temporary -File | ForEach-Object {Remove-Item -LiteralPath $_.FullName}
 Remove-Item -LiteralPath $temporary
}
