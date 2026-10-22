<#
.SYNOPSIS
    Checks the contents of the packed NuGet packages.

.DESCRIPTION
    The examples and tests consume the libraries through ProjectReference, so nothing else verifies what actually ends
    up in the .nupkg files. Every package must contain its assembly, its own README and the icon, and ship its build/
    props and targets. Win32Aot must also redistribute WebView2Loader.dll for every architecture (the build
    silently skips it if the Microsoft.Web.WebView2 package layout changes) and its rd.xml, and must not depend on
    Microsoft.Web.WebView2. Exit code 0 means every check passed.

.EXAMPLE
    pwsh tests/package/Test-Packages.ps1 -PackageDir artifacts/packages
#>
param([Parameter(Mandatory)] [string] $PackageDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$expected = @{
    'Scarlet.AspNetCore.Components.WebView.WindowsForms' = @{
        Assembly = 'Microsoft.AspNetCore.Components.WebView.WindowsForms.dll'
        Files = @()
    }
    'Scarlet.AspNetCore.Components.WebView.Wpf' = @{
        Assembly = 'Microsoft.AspNetCore.Components.WebView.Wpf.dll'
        Files = @()
    }
    'Scarlet.AspNetCore.Components.WebView.Win32Aot' = @{
        Assembly = 'Microsoft.AspNetCore.Components.WebView.Win32Aot.dll'
        Files = @(
            'runtimes/win-x64/native/WebView2Loader.dll',
            'runtimes/win-x86/native/WebView2Loader.dll',
            'runtimes/win-arm64/native/WebView2Loader.dll',
            'build/Scarlet.AspNetCore.Components.WebView.Win32Aot.rd.xml')
        RequiredDependencies = @('WebView2Aot', 'Microsoft.AspNetCore.Components.WebView')
        ForbiddenDependencies = @('Microsoft.Web.WebView2')
    }
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($id in $expected.Keys) {
    $spec = $expected[$id]
    $nupkg = Get-ChildItem $PackageDir -Filter "$id.*.nupkg" | Where-Object { $_.Name -match "^$([regex]::Escape($id))\.\d" -and $_.Name -notlike '*.snupkg' } | Select-Object -First 1
    if (-not $nupkg) { $failures.Add("${id}: package not found in $PackageDir"); continue }

    $zip = [IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $required = @("README.md", "icon.png", "build/$id.props", "build/$id.targets") + $spec.Files
        foreach ($file in $required) {
            if ($entries -notcontains $file) { $failures.Add("${id}: missing $file") }
        }
        if (-not ($entries | Where-Object { $_ -match "^lib/net[^/]+/$([regex]::Escape($spec.Assembly))$" })) {
            $failures.Add("${id}: missing lib/<tfm>/$($spec.Assembly)")
        }

        $readme = $zip.Entries | Where-Object FullName -eq 'README.md'
        if ($readme) {
            $reader = New-Object IO.StreamReader($readme.Open())
            $title = $reader.ReadLine()
            $reader.Close()
            if ($title -ne "# $id") { $failures.Add("${id}: README.md is not the package's own README (starts with '$title')") }
        }

        $icon = $zip.Entries | Where-Object FullName -eq 'icon.png'
        if ($icon -and $icon.Length -gt 1MB) { $failures.Add("${id}: icon.png is $([Math]::Round($icon.Length / 1MB, 1)) MB, NuGet allows at most 1 MB") }

        $nuspecEntry = $zip.Entries | Where-Object FullName -like '*.nuspec' | Select-Object -First 1
        $reader = New-Object IO.StreamReader($nuspecEntry.Open())
        [xml] $nuspec = $reader.ReadToEnd()
        $reader.Close()
        $dependencies = @($nuspec.package.metadata.dependencies.group.dependency | ForEach-Object { $_.id })
        foreach ($dependency in @($spec.RequiredDependencies)) {
            if ($dependency -and $dependencies -notcontains $dependency) { $failures.Add("${id}: missing dependency $dependency") }
        }
        foreach ($dependency in @($spec.ForbiddenDependencies)) {
            if ($dependency -and $dependencies -contains $dependency) { $failures.Add("${id}: must not depend on $dependency") }
        }
    }
    finally {
        $zip.Dispose()
    }

    Write-Host "Checked $($nupkg.Name)"
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host 'All packages look correct.'
