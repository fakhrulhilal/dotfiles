function Find-MSBuild {
    $VSEditions = 'Enterprise', 'Professional', 'Community', 'BuildTools'
    $VSVersions = '2019', '2017'

    foreach ($Version in $VSVersions) {
        foreach ($Edition in $VSEditions) {
            $MSBuildPath = [System.IO.Path]::Combine(${Env:ProgramFiles(x86)}, "Microsoft Visual Studio\$($Version)\$($Edition)\MSBuild\Current\Bin\MSBuild.exe")
            if (Test-Path -Path $MSBuildPath -PathType Leaf) {
                Write-Verbose "Using MSBuild from $MSBuildPath"
                return (Get-Item -Path $MSBuildPath)
            }
        }
    }

    $LegacyFramework = if ('AMD64' -eq $env:PROCESSOR_ARCHITECTURE) { 'Framework64'} Else { 'Framework' }
    $LegacyFolder = "${Env:Windir}\Microsoft.NET\$($LegacyFramework)"
    $VersionFolders = Get-ChildItem -Directory -Path $LegacyFolder | ?{ $_.Name -match '^v\d+' } | sort -Descending
    foreach ($Version in $VersionFolders) {
        $MSBuildPath = Join-Path -Path $Version.FullName -ChildPath 'MSBuild.exe'
        if (Test-Path -Path $MSBuildPath -PathType Leaf) {
            Write-Verbose "Using MSBuild from $MSBuildPath"
            return (Get-Item -Path $MSBuildPath)
        }
    }

    Write-Error 'No suiteable MSBuild command found'
}