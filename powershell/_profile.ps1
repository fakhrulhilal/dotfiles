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

$ThisScript = [System.IO.Path]::GetFileName($PSCommandPath)
$ScriptPath = Get-ActualPath -Path $PSCommandPath
$ScriptPath = $ScriptPath.Directory.FullName
$ConfigPath = (Get-Item -Path ([System.IO.Path]::Combine($ScriptPath, '..', 'config'))).FullName
$ErrorActionPreference = 'Stop'

$Scripts = Get-ChildItem -Path $ScriptPath -Filter *.ps1 -File | ?{ $ThisScript -ne $_.Name }
foreach ($Script in $Scripts) {
	. $Script.FullName
}

foreach ($Module in @('posh-git')) {
    Import-Module $Module
}
oh-my-posh init pwsh --config (Get-Item (Join-Path -Path $ConfigPath -ChildPath 'posh-theme.omp.json')).FullName | Invoke-Expression
$Env:PSModulePath = $Env:PSModulePath+";$ScriptPath\Modules"
