param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$releaseVersion = "1.0.0.2"
$projectRoot = $PSScriptRoot
$artifactRoot = Join-Path $projectRoot "artifacts"
$publishRoot = Join-Path $artifactRoot "publish"
$packageRoot = Join-Path $artifactRoot "package"
dotnet test (Join-Path $projectRoot "JellyLidarr.sln") --configuration $Configuration
dotnet publish (Join-Path $projectRoot "src/Jellyfin.Plugin.JellyLidarr/Jellyfin.Plugin.JellyLidarr.csproj") --configuration $Configuration --output $publishRoot --no-self-contained
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
$resolvedArtifacts = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPackage = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe package output path: $resolvedPackage" }
Get-ChildItem -LiteralPath $packageRoot -Force | Remove-Item -Recurse -Force
Copy-Item -LiteralPath (Join-Path $publishRoot "Jellyfin.Plugin.JellyLidarr.dll") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $publishRoot "Jellyfin.Plugin.JellyLidarr.deps.json") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot "Jellyfin.Plugin.JellyLidarr.png") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $publishRoot "Microsoft.Data.Sqlite.dll") -Destination $packageRoot
Copy-Item -Path (Join-Path $publishRoot "SQLitePCLRaw*.dll") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $publishRoot "runtimes") -Destination $packageRoot -Recurse
$zip = Join-Path $artifactRoot "jellylidarr_$releaseVersion.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
        $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
        $inputStream = $file.OpenRead()
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
} finally { $archive.Dispose() }
Write-Host "Created $zip"
