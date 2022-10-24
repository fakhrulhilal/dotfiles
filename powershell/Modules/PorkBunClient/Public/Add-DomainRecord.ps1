function Add-DomainRecord {
	[CmdletBinding()]
	param(
		[string]
		[Parameter(Mandatory=$True, HelpMessage='The domain zone')]
		$Domain,

		[string]
		[Parameter(Mandatory=$True)]
		[ValidateSet('A', 'AAAA', 'MX', 'TXT', 'CNAME', 'ALIAS', 'NS', 'SRV', 'TLS', 'CAA')]
		$RecordType,

		[string]
		[Parameter(Mandatory=$False, HelpMessage='The subdomain for the record being created, not including the domain itself. Leave blank to create a record on the root domain. Use * to create a wildcard record.')]
		$Name,

		[string]
		[Parameter(Mandatory=$True)]
		$Content,

		[int]
		[Parameter(Mandatory=$False, HelpMessage='Time to live in secondes')]
		$TimeToLive = 600,

        $Connection = $Script:PorkBunConnection
	)

	Process {
        Get-Response -Path "dns/create/$Domain" -Body @{
            name = $Name
			type = $RecordType
			content = $Content
			ttl = $TimeToLive
        } -Connection $Connection
	}
}
