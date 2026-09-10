param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

try {
    $parsedVersion = [Version]$Version.TrimStart('v', 'V')
} catch {
    throw "'$Version' is not a valid launcher version. Use Major.Minor.Patch or Major.Minor.Patch.Revision."
}

$revision = if ($parsedVersion.Revision -lt 0) { 0 } else { $parsedVersion.Revision }
$normalizedVersion = '{0}.{1}.{2}.{3}' -f $parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $revision
$repoRoot = Split-Path -Parent $PSScriptRoot
$changedFiles = [System.Collections.Generic.List[string]]::new()

$assemblyFiles = Get-ChildItem -Path (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tools') -Filter AssemblyInfo.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\SampleTheme\\' }

foreach ($file in $assemblyFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $updated = [regex]::Replace($content, '(?m)^(\[assembly:\s*AssemblyVersion\(")[^"]+("\)\])', {
        param($match) $match.Groups[1].Value + $normalizedVersion + $match.Groups[2].Value
    })
    $updated = [regex]::Replace($updated, '(?m)^(\[assembly:\s*AssemblyFileVersion\(")[^"]+("\)\])', {
        param($match) $match.Groups[1].Value + $normalizedVersion + $match.Groups[2].Value
    })

    if ($updated -ne $content) {
        Set-Content -LiteralPath $file.FullName -Value $updated -NoNewline
        $changedFiles.Add($file.FullName)
    }
}

$projectFiles = Get-ChildItem -Path (Join-Path $repoRoot 'src') -Filter *.csproj -Recurse
foreach ($file in $projectFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $updated = [regex]::Replace($content, '(?m)(<ApplicationVersion>)[^<]+(</ApplicationVersion>)', {
        param($match) $match.Groups[1].Value + $normalizedVersion + $match.Groups[2].Value
    })

    if ($updated -ne $content) {
        Set-Content -LiteralPath $file.FullName -Value $updated -NoNewline
        $changedFiles.Add($file.FullName)
    }
}

Write-Host "Launcher version set to $normalizedVersion. Updated $($changedFiles.Count) file(s)."
