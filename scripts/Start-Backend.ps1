param([switch]$Migrate, [string]$ImportReceipt)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = Get-Content (Join-Path $projectRoot '.local/database.json') -Raw | ConvertFrom-Json
$names = @('ConnectionStrings__Expenses', 'Telegram__BotToken', 'Auth__LocalKey', 'Auth__KeysPath', 'Auth__AllowHttp', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:ConnectionStrings__Expenses = "Host=$($settings.host);Port=$($settings.port);Database=$($settings.database);Username=$($settings.username);Password=$($settings.password)"
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'work/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'work/nuget'
    $telegramPath = Join-Path $projectRoot '.local/telegram.json'
    if (Test-Path $telegramPath) {
        $telegram = Get-Content $telegramPath -Raw | ConvertFrom-Json
        $env:Telegram__BotToken = $telegram.BotToken
        $env:Telegram__AdminUserId = [string]$telegram.AdminUserId
    }
    $keyPath = Join-Path $projectRoot '.local/development-login.key'
    if (!(Test-Path $keyPath)) {
        # Случайный ключ для браузера. Он не связан с токеном бота и не попадает в Git.
        $bytes = New-Object byte[] 32
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        [Convert]::ToBase64String($bytes) | Set-Content $keyPath -Encoding ascii
    }
    $env:Auth__LocalKey = (Get-Content $keyPath -Raw).Trim()
    $env:Auth__KeysPath = Join-Path $projectRoot '.local/data-protection'
    # Этот скрипт запускает API только локально. На хостинге оставляем Secure cookies.
    $env:Auth__AllowHttp = 'true'
    if ($Migrate) {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" -- --migrate
    } elseif ($ImportReceipt) {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" -- --import-receipt $ImportReceipt
    } else {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" --urls http://127.0.0.1:5080
    }
    if ($LASTEXITCODE -ne 0) { throw 'Команда сервера завершилась с ошибкой.' }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
