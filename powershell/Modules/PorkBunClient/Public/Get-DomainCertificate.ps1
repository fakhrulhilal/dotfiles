<#
.SYNOPSIS
    SSL Retrieve Bundle by Domain

.DESCRIPTION
    Retrieve the SSL certificate bundle for the domain.

.PARAMETER Domain
    Domain zone
#>
function Get-DomainCertificate {
    [CmdletBinding()]
    param (
		[string]
		[Parameter(Mandatory=$True, HelpMessage='The domain zone')]
        [ValidateNotNullOrEmpty()]
		$Domain,

        $Connection = $Script:PorkBunConnection
    )
       
    process {
        Get-Response -Path "ssl/retrieve/$Domain" -Connection $Connection
    }
}
