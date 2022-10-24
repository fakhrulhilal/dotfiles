function New-Connection {
    [CmdletBinding()]
    param (
        [string]
        $ApiKey = $Env:PorkBun_API_Key,

        [string]
        $ApiSecret = $Env:PorkBun_API_Secret,

        [string]
        $Endpoint = $Env:PorkBun_API_Endpoint
    )

    Process {
        $Connection = [PorkBunConnection]::new()
        $Connection.ApiKey = $ApiKey
        $Connection.ApiSecret = $ApiSecret
        $Connection.Endpoint = if ([string]::IsNullOrEmpty($Endpoint)) {
            'https://porkbun.com/api/json/v3'
        } else { 
            $Endpoint 
        }
        $Connection.Endpoint = $Connection.Endpoint.TrimEnd('/')
        $Script:PorkBunConnection = $Connection
    }
}
