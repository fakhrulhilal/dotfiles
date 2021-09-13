<#
.SUMMARY
    Get nuget CLI path or download the latest version when necessary
#>
function Find-Nuget {
    $Nuget = Get-Command -Name nuget.exe -ErrorAction SilentlyContinue
    if ($null -ne $Nuget) {
        return $Nuget
    }

    $Nuget = Get-Item -Path "${Env:LOCALAPPDATA}\nuget\nuget.exe" -ErrorAction SilentlyContinue
    if ($null -ne $Nuget) {
        return $Nuget
    }

    New-Item -ItemType Directory -Path "${Env:LOCALAPPDATA}\nuget" -Force | Out-Null
    
    $SavePath = "${Env:LOCALAPPDATA}\nuget\nuget.exe"
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $SavePath

    return (Get-Item -Path $SavePath)
}