function ConvertFrom-Har {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [string]
        [ValidateNotNullOrEmpty()]
        $Path
    )

    Process {
        Get-Content -Path $Path | Out-String | ConvertFrom-Json
    }
}