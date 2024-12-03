if ($null -eq (Get-Command oh-my-posh -WarningAction SilentlyContinue)) {
    brew install jandedobbeleer/oh-my-posh/oh-my-posh
}
foreach ($Module in @('posh-git')) {
    $Test = Get-Module -Name $Module -ListAvailable
    if ($null -eq $Test) {
        Write-Host "Getting required module: $Module"
        Install-Module -Name $Module -Scope CurrentUser -Force
    }
}
