function Start-WebProjects {
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$false)]
        [ValidateScript({ Test-Path -PathType Container -Path $_ })]
        $Path = (Get-Location)
    )

    Process {
        $Projects = Get-ChildItem -Path $Path -Filter *.csproj -Recurse | ?{ Test-WebProject $_ }
        foreach ($Project in $Projects) {
            Write-Verbose "Found web project: $($Project.FullName)"
            $Name = $Project.Name -replace '.csproj' -replace '\.', '_'
            Write-Verbose "Executing as $Name"
            $Script = [ScriptBlock]::Create("dotnet run --project $($Project.FullName)")
            Start-Job -Name $Name -ScriptBlock $Script
        }
    }
}

function Test-WebProject {
    param(
        [string]
        [Parameter(Mandatory=$true, ValueFromPipeline=$true)]
        [ValidateScript({ Test-Path -PathType Leaf -Path $_ })]
        $Path
    )

    Process {
        [xml]$Project = Get-Content -Path (Get-Item $Path)
        'Microsoft.NET.Sdk.Web' -eq $Project.Project.Sdk
    }
}