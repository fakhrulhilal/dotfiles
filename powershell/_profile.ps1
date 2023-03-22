function Get-ActualPath {
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

$ThisScript = Get-ActualPath -Path $PSCommandPath
$ScriptPath = $ThisScript.Directory.FullName
$AutoCompletionPath = Join-Path -Path $ScriptPath -ChildPath 'AutoCompletions'
$ConfigPath = (Get-Item -Path ([System.IO.Path]::Combine($ScriptPath, '..', 'config'))).FullName
$ErrorActionPreference = 'Stop'

Get-ChildItem -Path $ScriptPath -Filter *.ps1 -File | ?{ $ThisScript.Name -ne $_.Name } | %{ . $_ }
Get-ChildItem -Path $AutoCompletionPath -Filter *.ps1 -File | %{ . $_ }

foreach ($Module in @('posh-git')) {
    $Test = Get-Module -Name $Module -ListAvailable
    if ($null -eq $Test) {
        Write-Host "Getting required module: $Module"
        Install-Module -Name $Module -Scope CurrentUser -Force
    }

    Import-Module $Module
}

$OhMyPosh = Get-Command -Name oh-my-posh -WarningAction SilentlyContinue
if ($null -eq $OhMyPosh) {
    winget install JanDeDobbeleer.OhMyPosh -s winget
}
$Env:POSH_THEMES_PATH = (Get-Item (Join-Path -Path $ConfigPath -ChildPath 'posh-theme.omp.json')).FullName
oh-my-posh init pwsh | Invoke-Expression
$Env:PSModulePath = $Env:PSModulePath+";$ScriptPath\Modules"

'ThisScript', 'ScriptPath', 'AutoCompletionPath', 'ConfigPath' | %{ Remove-Variable -Name $_ }