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
