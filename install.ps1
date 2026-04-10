$repo = "iOfficeAI/OfficeCli"
$asset = "officecli-win-x64.exe"
$binary = "officecli.exe"

$source = $null

# Step 1: Try downloading from GitHub
$url = "https://github.com/$repo/releases/latest/download/$asset"
$checksumUrl = "https://github.com/$repo/releases/latest/download/SHA256SUMS"
$tempFile = "$env:TEMP\$binary"
Write-Host "Downloading OfficeCli..."
try {
    Invoke-WebRequest -Uri $url -OutFile $tempFile
    # Verify checksum if available
    $checksumOk = $false
    try {
        $checksumFile = "$env:TEMP\officecli-SHA256SUMS"
        Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumFile
        $checksumContent = Get-Content $checksumFile
        $expectedLine = $checksumContent | Where-Object { $_ -match $asset }
        if ($expectedLine) {
            $expected = ($expectedLine -split '\s+')[0]
            $actual = (Get-FileHash -Path $tempFile -Algorithm SHA256).Hash.ToLower()
            if ($expected -eq $actual) {
                $checksumOk = $true
                Write-Host "Checksum verified."
            } else {
                Write-Host "Checksum mismatch! Expected: $expected, Got: $actual"
                Remove-Item -Force $tempFile, $checksumFile -ErrorAction SilentlyContinue
                exit 1
            }
        }
        Remove-Item -Force $checksumFile -ErrorAction SilentlyContinue
    } catch {
        Write-Host "Checksum file not available, skipping verification."
    }
    $output = & $tempFile --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $source = $tempFile
        Write-Host "Download verified."
    } else {
        Write-Host "Downloaded file is not a valid OfficeCli binary."
        Remove-Item -Force $tempFile -ErrorAction SilentlyContinue
    }
} catch {
    Write-Host "Download failed."
}

# Step 2: Fallback to local files
if (-not $source) {
    Write-Host "Looking for local binary..."
    $candidates = @(".\$asset", ".\$binary", ".\bin\$asset", ".\bin\$binary", ".\bin\release\$asset", ".\bin\release\$binary")
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $output = & $candidate --version 2>&1
            if ($LASTEXITCODE -eq 0) {
                $source = $candidate
                Write-Host "Found valid binary at $candidate"
                break
            }
        }
    }
}

if (-not $source) {
    Write-Host "Error: Could not find a valid OfficeCli binary."
    Write-Host "Download manually from: https://github.com/$repo/releases"
    exit 1
}

# Step 3: Install
$existing = Get-Command $binary -ErrorAction SilentlyContinue
if ($existing) {
    $installDir = Split-Path $existing.Source
    Write-Host "Found existing installation at $($existing.Source), upgrading..."
} else {
    $installDir = "$env:LOCALAPPDATA\OfficeCli"
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Force $source "$installDir\$binary"

Remove-Item -Force $tempFile -ErrorAction SilentlyContinue

Write-Host "OfficeCli installed successfully!"
Write-Host "Binary path: $installDir\$binary"
Write-Host "No environment or agent configuration has been changed."
Write-Host "Optional next step: $installDir\$binary setup"

if (-not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) {
    $runSetup = Read-Host "Run optional setup now? [y/N]"
    if ($runSetup -match '^(?i:y|yes)$') {
        & "$installDir\$binary" setup
    } else {
        Write-Host "Skipped setup. You can run '$installDir\$binary setup' later."
    }
} else {
    Write-Host "Non-interactive install detected. Run '$installDir\$binary setup' later if you want PATH, skills, MCP, or auto-update."
}
