function Get-Response {
	[CmdletBinding()]
	param(
		[string]
		[Parameter(Mandatory=$True, HelpMessage='Endpoint path')]
		$Path,

        [hashtable]
        [Parameter(Mandatory=$False)]
        $Body,

        $Connection = $Script:PorkBunConnection
	)

    process {
        $Url = "$($Connection.Endpoint)/$Path"
        Write-Verbose "Calling endpoint $Url"
        if ($null -eq $Body) {
            $Body = @{}
        }
        $Body['secretapikey'] = $Connection.ApiSecret
        $Body['apikey'] = $Connection.ApiKey
        $Response = Invoke-RestMethod -Uri $Url -Method POST -Body (ConvertTo-Json $Body)
        if ('success') {
            Remove-Property -InputObject $Response -Names @('status')
        }
        else {
            $Response
        }
    }
}
