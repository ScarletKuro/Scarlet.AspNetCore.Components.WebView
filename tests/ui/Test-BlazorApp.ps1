<#
.SYNOPSIS
    UI smoke test for examples/Win32Aot.BlazorApp.

.DESCRIPTION
    Starts the app and drives it through UI Automation only (no mouse or keyboard input), so it runs on a CI runner
    or next to a user's desktop without interfering. Checks the pages, the "Background work" page
    (dotnet/maui#34855: windows closed while their component keeps working must not crash the app), and that the
    app exits cleanly. Screenshots of the windows are written to -OutDir. Exit code 0 means every check passed.

    Requires Windows PowerShell 5.1 (UIAutomationClient ships with .NET Framework).

.EXAMPLE
    powershell -File tests/ui/Test-BlazorApp.ps1 -Exe artifacts/win32aot-example/Win32Aot.BlazorApp.exe -ExpectNativeAot
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'Win32Aot.BlazorApp.UITest'),
    [switch] $ExpectNativeAot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$AnyElement = [System.Windows.Automation.Condition]::TrueCondition
$ErrorDialogTitle = 'Win32Aot.BlazorApp - Error'
$WM_CLOSE = 0x0010

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$results = New-Object System.Collections.ArrayList

function Check([string] $Name, [bool] $Ok, [string] $Detail = '') {
    $line = '{0}  {1}{2}' -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $(if ($Detail) { " ($Detail)" } else { '' })
    [void]$results.Add([pscustomobject]@{ Ok = $Ok; Line = $line })
    Write-Host $line
}

function Save-Screenshot([IntPtr] $Hwnd, [string] $Name) {
    if ($Hwnd -eq [IntPtr]::Zero) { return }
    $rect = New-Object Win32+RECT
    if (-not [Win32]::GetWindowRect($Hwnd, [ref]$rect)) { return }
    $bitmap = New-Object System.Drawing.Bitmap ([Math]::Max(1, $rect.Right - $rect.Left)), ([Math]::Max(1, $rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][Win32]::PrintWindow($Hwnd, $hdc, 2) # PW_RENDERFULLCONTENT, needed for WebView2 content
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save((Join-Path $OutDir "$Name.png"))
    $bitmap.Dispose()
}

function Wait-Until([scriptblock] $Probe, [int] $Seconds = 15) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $value = & $Probe
        if ($value) { return $value }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $null
}

function Find-ByName($Root, [string] $Name, [int] $Seconds = 15) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name)
    Wait-Until { $Root.FindFirst($Scope::Descendants, $condition) } $Seconds
}

function Find-Like($Root, [string] $Pattern, [int] $Seconds = 15) {
    Wait-Until { $Root.FindAll($Scope::Descendants, $AnyElement) | Where-Object { $_.Current.Name -like $Pattern } | Select-Object -First 1 } $Seconds
}

# Razor splits "Current count: @count" into separate text nodes, so match on the page's joined text instead.
function Wait-Text($Root, [string] $Regex, [int] $Seconds = 15) {
    Wait-Until { (($Root.FindAll($Scope::Descendants, $AnyElement) | ForEach-Object { $_.Current.Name }) -join ' ') -match $Regex } $Seconds
}

function Invoke-Element($Element) {
    $pattern = $null
    if ($Element -and $Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return $true
    }
    $false
}

function Invoke-ByName($Root, [string] $Name) { Invoke-Element (Find-ByName $Root $Name) }

function Find-TopLevelWindow([string] $Title, [int] $Seconds = 20) {
    $hwnd = Wait-Until { $h = [Win32]::FindWindow([NullString]::Value, $Title); if ($h -ne [IntPtr]::Zero) { $h } } $Seconds
    if ($hwnd) { $hwnd } else { [IntPtr]::Zero }
}

function Test-NoErrorDialog { [Win32]::FindWindow('#32770', $ErrorDialogTitle) -eq [IntPtr]::Zero }

# Log entries look like "13:51:11 #1: window closed" (HTML collapses the double space).
function Get-BackgroundLog($Root) {
    ($Root.FindAll($Scope::Descendants, $AnyElement) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d\d:\d\d:\d\d\s' }) -join "`n"
}

$process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -PassThru
$null = $process.Handle # keeps ExitCode available after exit
$mainHwnd = [IntPtr]::Zero
try {
    $mainHwnd = Wait-Until { $process.Refresh(); if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle } } 30
    if (-not $mainHwnd) { throw 'The main window did not open within 30 s.' }
    $main = $AE::FromHandle($mainHwnd)

    # Home
    Check 'Home page renders' ($null -ne (Find-ByName $main 'Blazor on WebView2Aot' 30))
    if ($ExpectNativeAot) {
        Check 'Runs as a NativeAOT executable' ($null -ne (Find-ByName $main 'Yes'))
    }
    Check 'Blazor error UI is hidden' ($null -eq (Find-Like $main '*An unhandled error has occurred*' 1))
    Save-Screenshot $mainHwnd '1-home'

    # Counter
    Check 'Counter page opens' (Invoke-ByName $main 'Counter')
    $clicks = 0
    foreach ($i in 1..3) { if (Invoke-ByName $main 'Click me') { $clicks++; Start-Sleep -Milliseconds 150 } }
    Check 'Counter increments' (($clicks -eq 3) -and [bool](Wait-Text $main 'Current count:\s+3\b'))
    Save-Screenshot $mainHwnd '2-counter'

    # JS interop
    Check 'JS interop page opens' (Invoke-ByName $main 'JS interop')
    Check '.NET -> JS returns browser info' ((Invoke-ByName $main 'Read browser info') -and ($null -ne (Find-ByName $main 'User agent')))
    Check 'JS -> .NET callback arrives' ((Invoke-ByName $main 'Ask JS to call .NET') -and ($null -ne (Find-Like $main 'Called back from JavaScript at*')))
    Save-Screenshot $mainHwnd '3-interop'

    # Background work (dotnet/maui#34855)
    Check 'Background work page opens' (Invoke-ByName $main 'Background work')
    $id = 0
    foreach ($variant in @(
            @{ Button = 'Open window (throws, DispatchExceptionAsync)'; Name = 'throws' },
            @{ Button = 'Open window (no exception)'; Name = 'no exception' })) {
        $id++
        $label = "[$($variant.Name)] window #$id"
        Check "$label opens" (Invoke-ByName $main $variant.Button)
        $childHwnd = Find-TopLevelWindow "Background work #$id - close me while loading"
        if ($childHwnd -eq [IntPtr]::Zero) { Check "$label found" $false; continue }

        Check "$label shows Loading..." ($null -ne (Find-ByName $AE::FromHandle($childHwnd) 'Loading...'))
        Save-Screenshot $childHwnd "4-background-$id"
        [void][Win32]::PostMessage($childHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)

        Check "$label work finishes after the window is gone" ($null -ne (Find-Like $main "*#${id}: background work finished*" 20))
        Start-Sleep -Milliseconds 1500 # leave time for late exceptions to surface
        $log = Get-BackgroundLog $main
        $closedAt = $log.IndexOf("#${id}: window closed")
        Check "$label closed before its work finished" (($closedAt -ge 0) -and ($closedAt -lt $log.IndexOf("#${id}: background work finished")))
        Check "$label raised no UnhandledException" ($log -notmatch "#${id}: UNHANDLED")
        Check "$label showed no error dialog" (Test-NoErrorDialog)
    }
    Save-Screenshot $mainHwnd '5-background-log'
    Write-Host "Background work log:`n$(Get-BackgroundLog $main)"

    # Shutdown
    [void]$process.CloseMainWindow()
    $exited = $process.WaitForExit(15000)
    Check 'App exits cleanly' ($exited -and $process.ExitCode -eq 0) $(if ($exited) { "exit code $($process.ExitCode)" } else { 'still running after 15 s' })
}
catch {
    Check 'Test script ran without errors' $false $_.Exception.Message
}
finally {
    if (-not $process.HasExited) {
        Save-Screenshot $mainHwnd 'failure-main'
        Save-Screenshot ([Win32]::FindWindow('#32770', $ErrorDialogTitle)) 'failure-error-dialog'
        $process.Kill()
    }
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ''
Write-Host "$($results.Count - $failed.Count)/$($results.Count) checks passed. Screenshots: $OutDir"
if ($failed.Count -gt 0 -or $results.Count -eq 0) { exit 1 }
exit 0
