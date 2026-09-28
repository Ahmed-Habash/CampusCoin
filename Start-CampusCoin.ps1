$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$workspaceDirectory = Split-Path (Split-Path $projectDirectory -Parent) -Parent
$localSdk = Join-Path $workspaceDirectory 'work/dotnet/dotnet.exe'
if (Test-Path $localSdk) {
    $env:DOTNET_ROOT = Split-Path $localSdk -Parent
    $env:DOTNET_CLI_HOME = Join-Path $workspaceDirectory 'work/cli'
    $env:NUGET_PACKAGES = Join-Path $workspaceDirectory 'work/packages'
    $dotnetCommand = $localSdk
} else {
    $dotnetCommand = 'dotnet'
}

# Optional Gemini failover keys (one per line). Folder is gitignored.
$geminiKeysFile = Join-Path $projectDirectory '.keys/gemini-keys.txt'
if (Test-Path $geminiKeysFile) {
    $keys = Get-Content $geminiKeysFile |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') }
    if ($keys.Count -gt 0) {
        $env:GEMINI_API_KEYS = ($keys -join ',')
        if (-not $env:GEMINI_API_KEY) { $env:GEMINI_API_KEY = $keys[0] }
    }
}

Push-Location $projectDirectory
try { & $dotnetCommand run } finally { Pop-Location }
