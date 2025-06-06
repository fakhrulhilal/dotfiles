<#
.SYNOPSIS
    DNS Retrieve Records by Domain

.DESCRIPTION
    Retrieve all editable DNS records associated with a domain

.PARAMETER Domain
    Domain zone

.PARAMETER RecordType
    The type of record being retrieved. 

.PARAMETER Name
    The subdomain for the record being retrieved, not including the domain itself.

.PARAMETER Content
    The answer content for the record. Please see the DNS management popup from the domain management console for proper formatting of each record type.

.PARAMETER TimeToLive
    The time to live in seconds for the record. The minimum and the default is 600 seconds.
#>
function Get-DomainRecords {
    [CmdletBinding()]
    param (
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
        Get-Response -Path "dns/retrieve/$Domain" -Connection $Connection
	}
}
