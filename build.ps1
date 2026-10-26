$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw 'C# compiler not found (requires .NET Framework 4.x).'
}

Push-Location $PSScriptRoot
try {
    $outTmp = "RdpMouseBridge.tmp.$([Guid]::NewGuid().ToString('N').Substring(0,8)).exe"
    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /debug- /codepage:65001 `
        /out:$outTmp `
        /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
        RdpMouseBridge.cs

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
