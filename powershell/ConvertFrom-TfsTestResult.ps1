<#
.SYNOPSIS
    Parse from Azure DevOps/TFS test result
#>
function ConvertFrom-TfsTestResult {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, Position=0)]
        [string]
        [ValidateScript({ Test-Path -Path $_ -PathType Leaf })]
        $Path
    )

    Begin {
        $TitleParser = [regex]::new('(?<wit>\w+)\s+(?<wit_no>\d+)\s+Test Case\s+(?<tc>\d+)\s+\-\s+(?<browser>\w+)\s\-\s(?<title>.+)')
    }

    Process {
        [xml]$Income = Get-Content -Path $Path
        $Output = @()
        foreach ($Result in $Income.TestRun.Results.UnitTestResult) {
            $Match = $TitleParser.Match($Result.testName)
            $Message = $null
            if ($null -ne $Result.Output.ErrorInfo) {
                $Message = $Result.Output.ErrorInfo.Message
            }
            $Row = New-Object psobject -Property @{
                Outcome = $Result.outcome
                Type = $Match.Groups['wit'].Value
                WorkItemNo = $Match.Groups['wit_no'].Value
                TC = $Match.Groups['tc'].Value
                Browser = $Match.Groups['browser'].Value
                Title = $Match.Groups['title'].Value
                StartTime = if ($null -eq $Result.startTime) { $null } else { [datetime]$Result.startTime }
                EndTime = if ($null -eq $Result.endTime) { $null } else { [datetime]$Result.endTime }
                Duration = if ($null -eq $Result.duration) { $null } else { [timespan]$Result.duration }
                Output = $Result.Output.StdOut
                Message = $Message
            }
            $Output += $Row
        }

        return $Output
    }
}