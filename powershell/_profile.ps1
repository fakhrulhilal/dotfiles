function Resolve-ActualPath {
    param(
        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        $Path
    )

    Process {
        $Item = Get-Item -Path $Path
        if ('SymbolicLink' -eq $Item.LinkType) {
            $FullPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Item.Directory.FullName, $Item.Target))
            return (Get-Item -Path $FullPath)
        }
        else {
            return $Item
        }
    }
}

$ThisScript = Resolve-ActualPath -Path $PSCommandPath
$ProfilePath = $ThisScript.Directory.FullName
$ScriptsPath = Join-Path -Path $ProfilePath -ChildPath 'Scripts'
$AutoCompletionPath = Join-Path -Path $ProfilePath -ChildPath 'AutoCompletions'
$ConfigPath = (Get-Item -Path ([System.IO.Path]::Combine($ProfilePath, '..', 'config'))).FullName
$ErrorActionPreference = 'Stop'

Get-ChildItem -Path $ScriptsPath -Filter *.ps1 -File | ?{ $ThisScript.Name -ne $_.Name } | %{ . $_ }
Get-ChildItem -Path $AutoCompletionPath -Filter *.ps1 -File | %{ . $_ }

foreach ($Module in @('posh-git')) {
    $Test = Get-Module -Name $Module -ListAvailable
    if ($null -eq $Test) {
        Write-Host "Getting required module: $Module"
        Install-Module -Name $Module -Scope CurrentUser -Force
    }

    Import-Module $Module
}

$OhMyPosh = Get-Command -Name oh-my-posh -ErrorAction SilentlyContinue
if ($null -eq $OhMyPosh) {
    winget install JanDeDobbeleer.OhMyPosh -s winget
}
$Env:POSH_THEMES_PATH = (Get-Item (Join-Path -Path $ConfigPath -ChildPath 'posh-theme.omp.json')).FullName
oh-my-posh init pwsh | Invoke-Expression
$Env:PSModulePath = $Env:PSModulePath+";$ScriptPath\Modules"

'ThisScript', 'ProfilePath', 'ScriptsPath', 'AutoCompletionPath', 'ConfigPath' | %{ Remove-Variable -Name $_ }