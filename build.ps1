$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw 'C# compiler not found (requires .NET Framework 4.x).'
}

Push-Location $PSScriptRoot
try {
    $outTmp = "RdpMouseBridge.tmp.$([Guid]::NewGuid().ToString('N').Substring(0,8)).exe"
    $cscArgs = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        '/optimize+',
        '/debug-',
        '/codepage:65001'
    )
    if (Test-Path "app.ico") {
        $cscArgs += "/win32icon:app.ico"
    }
    $cscArgs += "/out:$outTmp"
    $cscArgs += '/r:System.dll'
    $cscArgs += '/r:System.Core.dll'
    $cscArgs += '/r:System.Windows.Forms.dll'
    $cscArgs += '/r:System.Drawing.dll'
    $cscArgs += '/r:System.Web.Extensions.dll'
    $cscArgs += 'RdpMouseBridge.cs'

    & $csc $cscArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE."
    }

    if (Test-Path RdpMouseBridge.exe) {
        try {
            Remove-Item RdpMouseBridge.old.*.exe -Force -ErrorAction SilentlyContinue
            Move-Item RdpMouseBridge.exe RdpMouseBridge.old.exe -Force
            Remove-Item RdpMouseBridge.old.exe -Force -ErrorAction SilentlyContinue
        } catch {
            $backup = "RdpMouseBridge.old.$([Guid]::NewGuid().ToString('N').Substring(0,8)).exe"
            Move-Item RdpMouseBridge.exe $backup -Force
        }
    }
    Move-Item $outTmp RdpMouseBridge.exe -Force
    Write-Host 'Built RdpMouseBridge.exe successfully.'
}
finally {
    Pop-Location
}
