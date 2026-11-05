param([switch]$BuildLocal)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('AutoDealerQuotaCheck-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$savedTestConnection=$env:AUTODEALER_QUOTA_TEST_CONNECTION
$database='AutoDealerQuotaTest_'+[Guid]::NewGuid().ToString('N')
$master=$null;$created=$false
try {
 $site=[xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Web.config') -Raw)
 $entry=$site.configuration.connectionStrings.add|Where-Object {$_.name -eq 'AutoDealer.dev.Development'}
 $builder=[Data.SqlClient.SqlConnectionStringBuilder]::new($entry.connectionString)
 if($builder.DataSource -notmatch '^(localhost|\.|\(local\))(\\[^;]+)?$' -or !$builder.IntegratedSecurity){throw 'Only a local SQL instance with Windows authentication is accepted.'}
 $builder['Initial Catalog']='master';$master=[Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$master.Open()
 $command=$master.CreateCommand();$command.CommandText='CREATE DATABASE ['+$database+']';[void]$command.ExecuteNonQuery();$created=$true
 $builder['Initial Catalog']=$database;$testConnection=$builder.ConnectionString
 $connection=[Data.SqlClient.SqlConnection]::new($testConnection)
 try{
  $connection.Open()
  foreach($file in @('001_AutoDealerPlatform.sql','012_ApiKeyProtectedCredential.sql','013_ApiKeyMonthlyQuota.sql','013_ApiKeyMonthlyQuota.sql')){
   $migration=Get-Content -LiteralPath (Join-Path $repoRoot ('Database\'+$file)) -Raw
   # Migration001's legacy stored procedures are documentation, not installation SQL.
   $migration=[regex]::Replace($migration,'(?s)/\*.*?\*/','')
   foreach($batch in [regex]::Split($migration,'(?im)^\s*GO\s*\r?$')){if(!$batch.Trim()){continue};$command=$connection.CreateCommand();$command.CommandText=$batch;[void]$command.ExecuteNonQuery()}
  }
 }finally{$connection.Dispose()}
 $project=[xml](Get-Content -LiteralPath (Join-Path $repoRoot 'autodealer.dev.csproj') -Raw)
 $ns=[Xml.XmlNamespaceManager]::new($project.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/developer/msbuild/2003')
 $sources=$project.SelectNodes('//p:Compile',$ns)|ForEach-Object{Join-Path $repoRoot $_.GetAttribute('Include')}
 $references=@('/r:System.dll','/r:System.Core.dll','/r:System.Configuration.dll','/r:System.Data.dll','/r:System.Data.Linq.dll','/r:System.ComponentModel.DataAnnotations.dll','/r:System.Net.Http.dll','/r:System.Runtime.Caching.dll','/r:System.Web.dll','/r:System.Web.Extensions.dll','/r:System.Xml.dll','/r:System.Xml.Linq.dll','/r:System.Security.dll','/r:Microsoft.CSharp.dll')
 Get-ChildItem -LiteralPath (Join-Path $repoRoot 'bin') -Filter '*.dll' | Where-Object {$_.Name -ne 'autodealer.dev.dll'} | ForEach-Object {$references+=('/r:'+ $_.FullName);Copy-Item -LiteralPath $_.FullName -Destination $temporary}
 $compiler=Join-Path $repoRoot 'bin\roslyn\csc.exe';$assembly=Join-Path $temporary 'autodealer.dev.dll'
 & $compiler /nologo /langversion:7.3 /target:library ('/out:'+$assembly) @references @sources
 if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
 $exe=Join-Path $temporary 'ApiMonthlyQuotaRegression.exe'
 & $compiler /nologo /langversion:7.3 /target:exe ('/out:'+$exe) @references ('/r:'+$assembly) (Join-Path $repoRoot 'Tests\ApiMonthlyQuotaRegression.cs')
 if($LASTEXITCODE -ne 0){throw 'Quota regression compilation failed.'}
 $config=[xml]'<configuration><connectionStrings /></configuration>'
 foreach($name in @('AutoDealer.dev.Development','AutoDealer.dev.Production')){$node=$config.CreateElement('add');$node.SetAttribute('name',$name);$node.SetAttribute('connectionString',$testConnection);[void]$config.SelectSingleNode('/configuration/connectionStrings').AppendChild($node)}
 if($site.configuration.runtime){[void]$config.configuration.AppendChild($config.ImportNode($site.configuration.runtime,$true))}
 $config.Save($exe+'.config');$env:AUTODEALER_QUOTA_TEST_CONNECTION=$testConnection
 & $exe
 if($LASTEXITCODE -ne 0){throw 'Monthly quota integration checks failed.'}
 if($BuildLocal){Copy-Item -LiteralPath $assembly -Destination (Join-Path $repoRoot 'bin\autodealer.dev.dll') -Force;Write-Output 'Built monthly quota controls into local AutoDealer.dev.'}
} finally {
 $env:AUTODEALER_QUOTA_TEST_CONNECTION=$savedTestConnection
 [Data.SqlClient.SqlConnection]::ClearAllPools()
 if($created){if(!$database.StartsWith('AutoDealerQuotaTest_',[StringComparison]::Ordinal)){throw 'Unexpected cleanup database'};$command=$master.CreateCommand();$command.CommandText='ALTER DATABASE ['+$database+'] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ['+$database+'];';[void]$command.ExecuteNonQuery()}
 if($master){$master.Dispose()}
 Get-ChildItem -LiteralPath $temporary -File | ForEach-Object {Remove-Item -LiteralPath $_.FullName}
 Remove-Item -LiteralPath $temporary
}
