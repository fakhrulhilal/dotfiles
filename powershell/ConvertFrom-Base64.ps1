function ConvertFrom-Base64 {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [string]
        $Text
    )

    Process {
        [System.Text.Encoding]::Default.GetString([Convert]::FromBase64String($Text))
    }
}