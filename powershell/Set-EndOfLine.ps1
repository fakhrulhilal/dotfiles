function Set-EndOfLine {
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$true, ValueFromPipeline=$true)]
        [ValidateNotNullOrEmpty()]
        $Path,

        [ValidateSet('Unix', 'Windows', 'Mac')]
        [string]
        $Format = 'Windows',

        [switch]
        $RemoveLastLine
    )

    Begin {
        $LineEnding = switch ($Format) {
            'Unix' { "`n" }
            'Mac' { "`r" }
            Default { "`r`n" }
        }

        $LineEndingInfo = $LineEnding -replace "`r", 'CR' -replace "`n", 'LF'
        $Message = "Changing EOL of file {0} to $LineEndingInfo"
        if ($RemoveLastLine) {
            $Message += " along with trimming last line"
        }
    }

    Process {
        $Path = Get-Item -Path $Path
        Write-Verbose ([string]::Format($Message, $Path))
        $Content = [System.IO.File]::ReadAllText($Path)
        $Content = $Content -replace "(`r`n|`r|`n)", $LineEnding
        if ($RemoveLastLine) {
            $Content = ([string]$Content).TrimEnd()
        }
        [System.IO.File]::WriteAllText($Path, $Content)
    }
}