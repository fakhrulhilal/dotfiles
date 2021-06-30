function New-HMailConnection
{
    param(
        [string]
        [Parameter(Mandatory=$true, ParameterSetName='Username')]
        $Username,

        [securestring]
        [Parameter(Mandatory=$true, ParameterSetName='Username')]
        $Password,

        [pscredential]
        [Parameter(Mandatory=$true, ParameterSetName='Credential')]
        $Credential,

        [string]
        $Address = 'localhost'
    )

    $Connection = [HMailConnection]::new()
    $Connection.Address = $Address
    if ($null -ne $Credential) {
        $Connection.Account = $Credential
    }
    else {
        [pscredential]$Credential = New-Object System.Management.Automation.PSCredential($Username, $Password)
        $Connection.Account = $Credential
    }

    $Script:HMailConnection = $Connection
    return $Script:HMailConnection
}
