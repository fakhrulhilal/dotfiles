function Set-DBRecovery {
    param(
        [string]$Path,
        [string]$Recovery
    )
    $FullPath = Get-Item -Path $Path
    $DBProject = [XML](Get-Content -Path $FullPath)
    $RecoveryType = $DBProject.Project.PropertyGroup[0].ChildNodes | ?{ 'Recovery' -eq $_.Name }
    if ($null -eq $RecoveryType) {
        $RecoveryType = $DBProject.CreateElement('Recovery', $DBProject.Project.xmlns)
        $RecoveryType.InnerText = $Recovery
        $DBProject.Project.PropertyGroup[0].AppendChild($RecoveryType)
    }
    else {
        $DBProject.Project.PropertyGroup[0].Recovery = $Recovery
    }
    $DBProject.Save($FullPath)
}