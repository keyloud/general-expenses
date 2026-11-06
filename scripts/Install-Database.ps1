$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $projectRoot 'work/postgresql.zip'
$destination = Join-Path $projectRoot '.local/postgres'
if (-not (Test-Path "$destination/pgsql/bin/pg_ctl.exe")) {
    New-Item -ItemType Directory -Force (Split-Path $archive), (Split-Path $destination) | Out-Null
    # Официальная сборка EDB, указанная на странице бинарных выпусков PostgreSQL.
    Invoke-WebRequest 'https://get.enterprisedb.com/postgresql/postgresql-18.6-5-windows-x64-binaries.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
}
& "$PSScriptRoot/Start-Database.ps1"
