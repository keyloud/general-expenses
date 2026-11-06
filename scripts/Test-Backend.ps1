$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = Get-Content (Join-Path $projectRoot '.local/database.json') -Raw | ConvertFrom-Json
$previous = $env:EXPENSES_TEST_CONNECTION
try {
    $env:EXPENSES_TEST_CONNECTION = "Host=$($settings.host);Port=$($settings.port);Database=postgres;Username=$($settings.username);Password=$($settings.password)"
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'work/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'work/nuget'
    # Отдельный каталог сборки позволяет тестировать при работающем API в Windows.
    dotnet test "$projectRoot/backend.Tests/GeneralExpenses.Api.Tests.csproj" --artifacts-path "$projectRoot/work/test-artifacts"
    if ($LASTEXITCODE -ne 0) { throw 'Тесты завершились с ошибкой.' }
} finally { $env:EXPENSES_TEST_CONNECTION = $previous }
