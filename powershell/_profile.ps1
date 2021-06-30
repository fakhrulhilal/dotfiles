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

$Script:PhpBin = Get-Command -Name php -CommandType Application -ErrorAction SilentlyContinue
if ($null -ne $Script:PhpBin) {
  $Script:PhpConf = [System.IO.Path]::Combine(([System.IO.Path]::GetDirectoryName($Script:PhpBin.Source)), 'php.ini')
  $Script:ArcBin = 'D:\Aplikasi\arcanist\bin\arc'
  function Arc {
      &$Script:PhpBin -f "$($Script:ArcBin)" -c "$($Script:PhpConf)" -- $Args
  }
}

foreach ($Module in @('posh-git', 'oh-my-posh')) {
    $Test = Get-Module -Name $Module -ListAvailable
    if ($null -eq $Test) {
        Write-Host "Getting required module: $Module"
        Install-Module -Name $Module -Scope CurrentUser -Force
    }

    Import-Module $Module
}
Set-PoshPrompt -Theme (Get-Item (Join-Path -Path $ConfigPath -ChildPath 'posh-theme.omp.json')).FullName
