# Smoke test for CI: runs the self-check, then launches Countdown in each state and saves
# screenshots (full screen plus a 2x crop of the taskbar) and diagnostics to -OutDir.
# It changes the taskbar theme while it runs and puts the old value back.
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $OutDir
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
'@
[void][Win]::SetProcessDPIAware()

$key = 'HKCU:\Software\Countdown'
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$env:COUNTDOWN_DEBUG = '1'
$log = Join-Path $env:TEMP 'Countdown.log'
Remove-Item $log -ErrorAction SilentlyContinue

function Save-Shot([string] $Name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $taskbar = [Win]::FindWindow('Shell_TrayWnd', [NullString]::Value)
    if ($taskbar -ne [IntPtr]::Zero) {
        $r = New-Object 'Win+RECT'
        [void][Win]::GetWindowRect($taskbar, [ref] $r)
        $w = $r.Right - $r.Left
        $h = $r.Bottom - $r.Top
        $zoom = 2
        $crop = New-Object System.Drawing.Bitmap -ArgumentList ($w * $zoom), ($h * $zoom)
        $g = [System.Drawing.Graphics]::FromImage($crop)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $src = New-Object System.Drawing.Rectangle -ArgumentList ($r.Right - $w - $screen.Left), ($r.Top - $screen.Top), $w, $h
        $dst = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, ($w * $zoom), ($h * $zoom)
        $g.DrawImage($bitmap, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
        $crop.Save((Join-Path $OutDir "$Name-taskbar.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose()
        $crop.Dispose()
    }
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "screenshot: $Name"
}

function Invoke-Cli([string] $Argument, [string] $Name) {
    $out = Join-Path $OutDir "$Name.txt"
    $p = Start-Process -FilePath $Exe -ArgumentList $Argument -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out
    Get-Content $out -Encoding UTF8 | Write-Host
    return $p.ExitCode
}

function Start-Countdown {
    $p = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 5
    if ($p.HasExited) { throw "Countdown exited early with code $($p.ExitCode)" }
    return $p
}

function Stop-Countdown($p) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# New-Item -Force on an existing registry key replaces it with an empty one, so only create
# keys that are missing.
function Confirm-Key([string] $Path) {
    if (-not (Test-Path $Path)) { New-Item -Path $Path | Out-Null }
}

function Set-Countdown([string] $Name, [datetime] $WhenUtc) {
    Confirm-Key $key
    New-ItemProperty -Path $key -Name EventName -Value $Name -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $key -Name Target -Value $WhenUtc.ToString('o') -PropertyType String -Force | Out-Null
}

$script:Failed = $false
function Fail([string] $Message) {
    Write-Host "FAIL: $Message"
    $script:Failed = $true
}

function Get-Label {
    $taskbar = [Win]::FindWindow('Shell_TrayWnd', [NullString]::Value)
    return [Win]::FindWindowEx($taskbar, [IntPtr]::Zero, [NullString]::Value, 'CountdownTaskbarLabel')
}

function Get-LabelRect {
    $handle = Get-Label
    if ($handle -eq [IntPtr]::Zero) { return $null }
    $r = New-Object 'Win+RECT'
    [void][Win]::GetWindowRect($handle, [ref] $r)
    return $r
}

# Runs --diagnose, fails if the label overlaps any taskbar button, and returns its left edge.
function Test-LabelClear([string] $Name) {
    [void](Invoke-Cli '--diagnose' "diagnose-$Name")
    $text = Get-Content (Join-Path $OutDir "diagnose-$Name.txt") -Raw -Encoding UTF8
    $m = [regex]::Match($text, 'Running label 0x[0-9A-F]+ \[(-?\d+),(-?\d+) (\d+)x(\d+)\]')
    if (-not $m.Success) { Fail "${Name}: the label is not on the taskbar"; return -1 }
    $left = [int]$m.Groups[1].Value
    $right = $left + [int]$m.Groups[3].Value
    $buttons = [regex]::Match($text, 'buttons: ([0-9\- ]+)')
    if ($buttons.Success) {
        foreach ($span in $buttons.Groups[1].Value.Trim().Split(' ')) {
            $edges = $span.Split('-')
            if ([int]$edges[0] -lt $right -and [int]$edges[1] -gt $left) {
                Fail "${Name}: the label [$left-$right] overlaps the taskbar button at $span"
            }
        }
    }
    Write-Host "${Name}: label at $left-$right"
    return $left
}

function Set-AutoHide([bool] $On) {
    $data = New-Object 'Win+APPBARDATA'
    $data.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type] 'Win+APPBARDATA')
    $data.hWnd = [Win]::FindWindow('Shell_TrayWnd', [NullString]::Value)
    $state = 2                 # ABS_ALWAYSONTOP: auto-hide off
    if ($On) { $state = 1 }    # ABS_AUTOHIDE
    $data.lParam = [IntPtr] $state
    [void][Win]::SHAppBarMessage(0xA, [ref] $data)    # ABM_SETSTATE
    Start-Sleep -Seconds 1
}

function Set-TaskbarTheme([int] $Light) {
    Confirm-Key $personalize
    New-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value $Light -PropertyType DWord -Force | Out-Null
    $result = [IntPtr]::Zero
    [void][Win]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [IntPtr]::Zero, 'ImmersiveColorSet', 2, 5000, [ref] $result)
    Start-Sleep -Seconds 3
}

Write-Host '== self-check'
$code = Invoke-Cli '--check' 'check'
if ($code -ne 0) { throw "self-check failed with code $code" }

Write-Host '== diagnostics before launch'
[void](Invoke-Cli '--diagnose' 'diagnose-before')

Write-Host '== first launch: default countdown, settings window open'
Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
$app = Start-Countdown
Save-Shot 'first-launch'
Stop-Countdown $app

Write-Host '== a saved countdown, hover and menu'
Set-Countdown 'Trip to Japan' ([datetime]::UtcNow.AddDays(18).AddHours(3).AddMinutes(12).AddSeconds(30))
$app = Start-Countdown
Save-Shot 'countdown'
[void](Invoke-Cli '--diagnose' 'diagnose-running')
$taskbar = [Win]::FindWindow('Shell_TrayWnd', [NullString]::Value)
# PowerShell passes $null to a string parameter as "", which FindWindowEx would match
# literally; [NullString]::Value is a real null.
$label = [Win]::FindWindowEx($taskbar, [IntPtr]::Zero, [NullString]::Value, 'CountdownTaskbarLabel')
if ($label -eq [IntPtr]::Zero) {
    Write-Host 'label window not found'
} else {
    $r = New-Object 'Win+RECT'
    [void][Win]::GetWindowRect($label, [ref] $r)
    $x = [int](($r.Left + $r.Right) / 2)
    $y = [int](($r.Top + $r.Bottom) / 2)
    [void][Win]::SetCursorPos($x, $y)
    [Win]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 800
    Save-Shot 'hover'
    [Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 1
    Save-Shot 'menu'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    [void][Win]::SetCursorPos(10, 10)
    Start-Sleep -Milliseconds 500
}

Write-Host '== light taskbar'
$previousTheme = (Get-ItemProperty -Path $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue).SystemUsesLightTheme
Set-TaskbarTheme 1
Save-Shot 'light'
if ($null -eq $previousTheme) { $previousTheme = 0 }
Set-TaskbarTheme $previousTheme

Write-Host '== placement: left, clear of every taskbar button'
$normalLeft = Test-LabelClear 'placement'

Write-Host '== auto-hiding taskbar: the label comes back with it, in place and on top'
$screenHeight = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height

function Test-TaskbarShown {
    $r = New-Object 'Win+RECT'
    [void][Win]::GetWindowRect([Win]::FindWindow('Shell_TrayWnd', [NullString]::Value), [ref] $r)
    return ($r.Top -ge 0 -and $r.Bottom -le $screenHeight)
}

# Milliseconds until the condition holds, or -1 after the timeout.
function Wait-Until([scriptblock] $Condition, [int] $TimeoutMs) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return [int] $watch.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 20
    }
    return -1
}

# The label is up when it is visible, fully on screen, where it was before, and the top child of
# the taskbar (GW_HWNDPREV is null), so none of Explorer's own windows covers it.
function Test-LabelUp {
    $handle = Get-Label
    if ($handle -eq [IntPtr]::Zero -or -not [Win]::IsWindowVisible($handle)) { return $false }
    $r = New-Object 'Win+RECT'
    [void][Win]::GetWindowRect($handle, [ref] $r)
    if ($r.Top -lt 0 -or $r.Bottom -gt $screenHeight -or [math]::Abs($r.Left - $normalLeft) -gt 1) { return $false }
    return ([Win]::GetWindow($handle, 3) -eq [IntPtr]::Zero)
}

# Reveals an auto-hidden taskbar: first with the pointer at the bottom edge at x, as a person
# does, then with Win+T. Returns how, or $null when this machine would not show it.
function Show-HiddenTaskbar([int] $X) {
    [void][Win]::SetCursorPos($X, $screenHeight - 30)
    Start-Sleep -Milliseconds 50
    [void][Win]::SetCursorPos($X, $screenHeight - 1)
    if ((Wait-Until { Test-TaskbarShown } 2500) -ge 0) { return 'pointer' }
    [Win]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
    [Win]::keybd_event(0x54, 0, 0, [UIntPtr]::Zero)
    [Win]::keybd_event(0x54, 0, 2, [UIntPtr]::Zero)
    [Win]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
    if ((Wait-Until { Test-TaskbarShown } 2500) -ge 0) { return 'Win+T' }
    return $null
}

if (Test-LabelUp) { Write-Host 'before auto-hide: the label is on top of the taskbar' } else { Fail 'before auto-hide: the label is not up' }
Set-AutoHide $true
[void][Win]::SetCursorPos(400, 300)
if ((Wait-Until { -not (Test-TaskbarShown) } 5000) -lt 0) { Write-Host 'note: the taskbar did not hide' }
Save-Shot 'autohide-hidden'
# Reveals 2 and 3 hover right over the label's sliver of the hidden taskbar, which must not stop
# the taskbar from popping up; reveals 1 and 4 hover elsewhere, for comparison.
$hidden = Get-LabelRect
$overLabel = [int](($hidden.Left + $hidden.Right) / 2)
$revealX = @(0, 400, $overLabel, $overLabel, 400)
$revealedBy = @{}
for ($i = 1; $i -le 4; $i++) {
    $how = Show-HiddenTaskbar $revealX[$i]
    $revealedBy[$i] = $how
    if ($null -eq $how) {
        Write-Host "reveal ${i}: this machine would not reveal the taskbar; skipped"
    } else {
        $after = Wait-Until { Test-LabelUp } 1000
        if ($i -eq 1) { Save-Shot 'autohide-revealed' }
        if ($after -lt 0) {
            $r = Get-LabelRect
            Fail "reveal ${i} (by ${how}): the label was not up within 1 s of the taskbar (at $($r.Left),$($r.Top); top child: $([Win]::GetWindow((Get-Label), 3) -eq [IntPtr]::Zero))"
        } elseif ($after -gt 300) {
            Fail "reveal ${i} (by ${how}): the label took $after ms to come up after the taskbar"
        } else {
            Write-Host "reveal ${i} (by ${how}): the label was up $after ms after the taskbar"
        }
    }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    [void][Win]::SetCursorPos(400, 300)
    [void](Wait-Until { -not (Test-TaskbarShown) } 5000)
}
if ($revealedBy[4] -eq 'pointer' -and $revealedBy[2] -ne 'pointer' -and $revealedBy[3] -ne 'pointer') {
    Fail "hovering over the label's sliver did not reveal the taskbar, while hovering elsewhere did"
}
Set-AutoHide $false
Start-Sleep -Seconds 4
Save-Shot 'autohide-off'
$afterLeft = Test-LabelClear 'after-autohide'
if ($afterLeft -ne $normalLeft) { Fail "after auto-hide was turned off the label sits at x=$afterLeft instead of x=$normalLeft" }
Stop-Countdown $app

Write-Host '== finished countdown, then a second launch opens settings'
Set-Countdown 'Trip to Japan' ([datetime]::UtcNow.AddHours(-1))
$app = Start-Countdown
Save-Shot 'finished'
Start-Process -FilePath $Exe | Out-Null
Start-Sleep -Seconds 3
Save-Shot 'second-launch'
Stop-Countdown $app

Write-Host '== log'
if (Test-Path $log) {
    Copy-Item $log (Join-Path $OutDir 'Countdown.log')
    Get-Content $log | Write-Host
}

if ($script:Failed) { throw 'smoke test failed' }
Write-Host '== all checks passed'

