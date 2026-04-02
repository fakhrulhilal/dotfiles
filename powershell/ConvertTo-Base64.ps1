function ConvertTo-Base64 {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [string]
        $Text
    )
    
    Process {
        [Convert]::ToBase64String([System.Text.Encoding]::Default.GetBytes($Text))
    }
}