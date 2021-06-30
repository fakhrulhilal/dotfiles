<#
.SYNOPSIS
    Find untranslated texts from resource files
#>
function Import-ResourceTranslation {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$false)]
        [string]
        $Path = (Get-Location),

        [string]
        $Filter = 'Resource.*resx'
    )

    Begin {
        $LangParser = [regex]::new('Resource\.((?<lang>\w+(\-\w+)?)\.)?resx')
        function Get-Language {
            param(
                [string]
                [Parameter(Position=0,ValueFromPipeline=$true)]
                $FileName
            )

            Process {
                $Match = $LangParser.Match($FileName)
                if (-not($Match.Success)) {
                    return $null
                }

                if ($Match.Groups['lang'].Success) {
                    return $Match.Groups['lang'].Value
                } else {
                    return '#default#'
                }
            }
        }
    }

    Process {
        $Files = @(Get-ChildItem -Path $Path -Filter $Filter)
        $Resources = $Files | Select-Object -Property @{l='Language';e={(Get-Language -FileName $_.Name)}},@{l='Path';e={$_.FullName}} | sort 'Language'
        $AllTranslations = @{}
        $Resources | foreach { $AllTranslations[$_.Language] = @{} }
        foreach ($File in $Resources) {
            [xml]$Resource = Get-Content -Path $File.Path
            $Resource.root.data | foreach { $AllTranslations[$File.Language][$_.name] = $_.value }
        }

        $DefaultTranslation = $AllTranslations['#default#']
        $AllTranslations.Remove('#default#')
        $Output = @()
        foreach ($ResourceName in $DefaultTranslation.Keys) {
            $Row = @{
                Text = $ResourceName
                'Default' = $DefaultTranslation[$ResourceName]
            }
            foreach ($Language in $AllTranslations.Keys) {
                $Row[$Language] = if ($AllTranslations[$Language].ContainsKey($ResourceName)) {
                    $AllTranslations[$Language][$ResourceName]
                } else {
                    ''
                }
            }
            $Output += (New-Object psobject -Property $Row)
        }

        return $Output
    }
}