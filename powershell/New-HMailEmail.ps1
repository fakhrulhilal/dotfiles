function New-HMailEmail
{
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$True)]
        $From,

        [string]
        [Parameter(Mandatory=$True)]
        $To,

        [string]
        $Subject,

        [string]
        $Body,

        [string[]]
        $Attachments,

        [string]
        $Server = 'localhost'
    )

    $IsLocalServer = @('localhost', '127.0.0.1') -contains $Server
    Add-Type -Path "$PSScriptRoot\Interop.hMailServer.dll"
    if ($IsLocalServer) {
        $HMailMessage = [hMailServer.MessageClass]::new()
    }
    else {
        $HMailType = [System.Type]::GetTypeFromProgID('hMailServer.Message', $Server)
        $HMailMessage = [hMailServer.MessageClass][System.Activator]::CreateInstance($HMailType)
    }
    $EmailRegex = [regex]::new('((?<display_name>[^<]+)\s)?<?(?<email>[^@]+@[\w\.]+)>?(;\s)?')
    $MatchFrom = $EmailRegex.Match($From)
    if (!$MatchFrom.Success) {
        Write-Error "Invalid FROM address: $From"
    }
    $HMailMessage.From = $MatchFrom.Value
    $HMailMessage.FromAddress = $MatchFrom.Groups['email'].Value
    $MatchesTo = $EmailRegex.Matches($To)
    if ($MatchesTo.Count -lt 1) {
        Write-Error "Invalid TO address: $To"
    }
    foreach ($Match in $MatchesTo) {
        $DisplayName = if ($Match.Groups['display_name'].Success) { $Match.Groups['display_name'] } else { [string]::Empty }
        $HMailMessage.AddRecipient($DisplayName, $Match.Groups['email'].Value)
    }
    $HMailMessage.Subject = $Subject
    $HMailMessage.Body = $Body
    if ($Attachments.Count -gt 0) {
        foreach ($Attachment in $Attachments) {
            $HMailMessage.Attachments.Add($Attachment)
        }
    }
    
    $HMailMessage.Save()
    return $HMailMessage
}