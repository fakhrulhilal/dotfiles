function Build-VisualStudio
{
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Position=0, ValueFromPipeline=$true)]
        $Path,

        [switch]
        $Force,

        [string]
        [ValidateSet('Debug', 'Release')]
        $Configuration = 'Debug',

        [string]
        [Parameter(Mandatory=$false)]
        [ValidateSet('5', '6', '7', '7.1', '7.2', '7.3', '8', '9', 'latest')]
        $LangVersion
    )

    Begin {
        function Find-BuildFile {
            param([string]$Path, [string[]]$Filters)
            
            foreach ($Filter in $Filters) {
                $Files = if ([string]::IsNullOrEmpty($Path)) { 
                    Get-ChildItem $Filter 
                } else { 
                    Get-ChildItem -Path $Path -Filter $Filter 
                }
                if ($Files -is [array] -and $Files.Length -gt 0) {
                    return $Files[0].FullName
                } elseif ($null -ne $Files ) {
                    return $Files
                }
            }
        }
    }

    Process {
        if ([string]::IsNullOrEmpty($Path)) {
            $Path = Find-BuildFile -Filters '*.sln', '*.csproj', '*.sqlproj'
        }
        elseif (Test-Path -Path $Path -PathType Container) {
            $Path = Find-BuildFile -Path $Path -Filters '*.sln', '*.csproj', '*.sqlproj'
        }
        if ([string]::IsNullOrEmpty($Path)) {
            Write-Error "No solution/project found in this folder"
        }

        Write-Verbose "Building: $Path"
        $Target = 'build'
        $Path = Get-Item -Path $Path
        $BaseFolder = $Path.Directory.FullName
        if ($Force) {
            $Target = 'rebuild'
            $NugetPackageFolder = "$BaseFolder\packages"
            if (Test-Path -Path $NugetPackageFolder -PathType Container) {
                Remove-Item $NugetPackageFolder -Force -Recurse
            }
            Get-ChildItem -Path $BaseFolder -Include bin,obj -Directory -Recurse | Remove-Item -Force -Recurse
            &nuget restore $Path
        }

        if ($PSBoundParameters.ContainsKey('LangVersion')) {
            &msbuild $Path /t:$Target /p:Configuration=$Configuration /p:LangVersion=$LangVersion
        }
        else {
            &msbuild $Path /t:$Target /p:Configuration=$Configuration
        }
    }
}
