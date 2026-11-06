param([switch]$Stop)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $projectRoot '.local'
$bin = Join-Path $runtime 'postgres/pgsql/bin'
$data = Join-Path $runtime 'pgdata'
$settingsPath = Join-Path $runtime 'database.json'

if (-not (Test-Path "$bin/pg_ctl.exe")) {
    throw 'Сначала распакуйте PostgreSQL: см. README, раздел первоначальной установки.'
}
if ($Stop) {
    & "$bin/pg_ctl.exe" -D $data -m fast -w stop
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось остановить PostgreSQL.' }
    return
}

if (-not (Test-Path $settingsPath)) {
    # Случайный пароль хранится только в игнорируемой .local, не в исходном коде.
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    @{ host = '127.0.0.1'; port = 55432; username = 'expenses'; password = $password; database = 'general_expenses' } |
        ConvertTo-Json | Set-Content -LiteralPath $settingsPath
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if (-not (Test-Path "$data/PG_VERSION")) {
    $passwordFile = Join-Path $runtime 'init-password.tmp'
    try {
        Set-Content -LiteralPath $passwordFile -Value $settings.password -Encoding ascii
        & "$bin/initdb.exe" -D $data -U $settings.username --pwfile=$passwordFile --auth=scram-sha-256 --encoding=UTF8 --locale=C
        if ($LASTEXITCODE -ne 0) { throw 'initdb завершился с ошибкой.' }
        Add-Content "$data/postgresql.conf" "`nlisten_addresses = '127.0.0.1'`nport = 55432"
    } finally {
        if (Test-Path $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    }
}
& "$bin/pg_ctl.exe" -D $data status *> $null
if ($LASTEXITCODE -ne 0) {
    # Запускаем фоновый процесс без отдельного окна; данные остаются в проекте.
    $arguments = @('-D', ('"' + $data + '"'), '-l', ('"' + (Join-Path $runtime 'postgres.log') + '"'), '-w', 'start')
    $process = Start-Process -FilePath "$bin/pg_ctl.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # WaitForExit ждёт только pg_ctl; Start-Process -Wait ожидал бы и сам сервер.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Не удалось запустить PostgreSQL; смотрите .local/postgres.log.' }
}
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = $settings.password
    $exists = & "$bin/psql.exe" -h $settings.host -p $settings.port -U $settings.username -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'general_expenses'"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось подключиться к PostgreSQL.' }
    if ($exists -ne '1') {
        & "$bin/createdb.exe" -h $settings.host -p $settings.port -U $settings.username $settings.database
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать базу.' }
    }
} finally { $env:PGPASSWORD = $previousPassword }
Write-Host 'PostgreSQL готов: 127.0.0.1:55432, база general_expenses.'

