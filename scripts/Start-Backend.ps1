param([switch]$Migrate, [string]$ImportReceipt)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = Get-Content (Join-Path $projectRoot '.local/database.json') -Raw | ConvertFrom-Json
$previousConnection = $env:ConnectionStrings__Expenses
try {
    $env:ConnectionStrings__Expenses = "Host=$($settings.host);Port=$($settings.port);Database=$($settings.database);Username=$($settings.username);Password=$($settings.password)"
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'work/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'work/nuget'
    if ($Migrate) {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" -- --migrate
    } elseif ($ImportReceipt) {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" -- --import-receipt $ImportReceipt
    } else {
        dotnet run --no-launch-profile --project "$projectRoot/backend/GeneralExpenses.Api.csproj" --urls http://127.0.0.1:5080
    }
    if ($LASTEXITCODE -ne 0) { throw 'Команда сервера завершилась с ошибкой.' }
} finally { $env:ConnectionStrings__Expenses = $previousConnection }

