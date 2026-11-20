#Requires -Version 7.0
param([switch]$Stop)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$frontend = Join-Path $projectRoot 'frontend'
$backend = Join-Path $projectRoot 'backend/GeneralExpenses.Api.csproj'
$artifacts = Join-Path $projectRoot 'work/telegram-preview'
$api = Join-Path $artifacts 'bin/GeneralExpenses.Api/release/GeneralExpenses.Api.dll'

if ($Stop) {
    # Туннель и API могут быть открыты в отдельных терминалах. Находим их по
    # пути проекта и аргументам запуска, поскольку PID меняется при перезапуске.
    $tunnelExe = Join-Path $projectRoot '.local/devtunnel.exe'
    $tunnels = Get-CimInstance Win32_Process -Filter "Name = 'devtunnel.exe'" | Where-Object {
        $_.ExecutablePath -eq $tunnelExe -and
        $_.CommandLine -match '\s+"?host"?\s+"?porovnu-expenses-preview(?:\.eun1)?"?(?:\s|$)'
    }
    $apiArgument = '(?:^|\s)"?' + [regex]::Escape($api) + '"?(?:\s|$)'
    $servers = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Replace('/', '\') -match $apiArgument -and $_.CommandLine -match '\s--urls\s'
    }
    foreach ($process in (@($tunnels) + @($servers))) {
        if ($null -eq $process) { continue }
        Stop-Process -Id $process.ProcessId -PassThru | Wait-Process -Timeout 10
    }
    # pg_ctl корректно завершает запись и останавливает кластер проекта.
    & "$PSScriptRoot/Start-Database.ps1" -Stop
    Write-Host 'Предпросмотр Telegram остановлен.'
    return
}

$names = @('ConnectionStrings__Expenses','Telegram__BotToken','Auth__AllowHttp','Auth__LocalKey','Auth__KeysPath','DOTNET_CLI_HOME','NUGET_PACKAGES','ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

try {
    & "$PSScriptRoot/Start-Database.ps1"
    $settings = Get-Content "$projectRoot/.local/database.json" -Raw | ConvertFrom-Json
    $bot = Get-Content "$projectRoot/.local/telegram.json" -Raw | ConvertFrom-Json
    if (![string]::IsNullOrWhiteSpace($bot.BotToken)) {
        $env:Telegram__BotToken = $bot.BotToken
    } else { throw 'Заполните BotToken в .local/telegram.json.' }
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'work/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'work/nuget'

    # Собираем статику для телефона. Vite dev server остаётся доступен для локальной разработки.
    & npm.cmd --prefix $frontend run build
    if ($LASTEXITCODE) { throw 'Не удалось собрать React.' }
    & dotnet build $backend -c Release --artifacts-path $artifacts
    if ($LASTEXITCODE) { throw 'Не удалось собрать API.' }

    # Оба режима берут имя рабочей базы из одного файла .local/database.json.
    $env:ConnectionStrings__Expenses = "Host=$($settings.host);Port=$($settings.port);Database=$($settings.database);Username=$($settings.username);Password=$($settings.password)"
    $env:Auth__AllowHttp = 'false'
    $env:Auth__LocalKey = $null
    $env:Auth__KeysPath = Join-Path $projectRoot '.local/telegram-preview-keys'
    $env:ASPNETCORE_ENVIRONMENT = $env:DOTNET_ENVIRONMENT = 'Production'
    & dotnet $api --migrate
    if ($LASTEXITCODE) { throw 'Не удалось применить миграции к рабочей базе.' }
    # HTTPS завершается на стороне туннеля. Сервер слушает только loopback.
    # Обработчик чата запускается в одном экземпляре вместе с Telegram-версией API.
    $miniAppUrl = $bot.MiniAppUrl ?? 'https://91gz75cb-5082.eun1.devtunnels.ms/'
    & dotnet $api --Telegram:MiniAppUrl $miniAppUrl --Telegram:AdminUserId ([long]$bot.AdminUserId) --urls http://127.0.0.1:5082 --webroot "$frontend/dist"
    if ($LASTEXITCODE) { throw 'Сервер предпросмотра завершился с ошибкой.' }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
