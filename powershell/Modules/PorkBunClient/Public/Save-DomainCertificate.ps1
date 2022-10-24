<#
.SYNOPSIS
    Save SSL certificate Bundle by Domain

.DESCRIPTION
    Save the SSL certificate bundle for the domain.

.PARAMETER Domain
    Domain zone

.PARAMETER Path
    Directory to store the certificate bundle
#>
function Save-DomainCertificate {
    [CmdletBinding()]
    [OutputType([void])]
    param (
		[string]
		[Parameter(Mandatory=$True, HelpMessage='The domain zone')]
        [ValidateNotNullOrEmpty()]
		$Domain,

        [string]
        [Parameter(Mandatory=$True)]
        [ValidateScript({ Test-Path -PathType Container -Path $_ })]
        $Path,

        $Connection = $Script:PorkBunConnection
    )

    process {
        $Response = Get-DomainCertificate -Domain $Domain -Connection $Connection

        $File = Join-Path -Path $Path -ChildPath "$Domain.private.key.pem"
        Write-Verbose "Writing private key file to $File"
        Set-Content -Path $File -Value $Response.privatekey

        $File = Join-Path -Path $Path -ChildPath "$Domain.public.key.pem"
        Write-Verbose "Writing public key file to $File"
        Set-Content -Path $File -Value $Response.publickey

        $File = Join-Path -Path $Path -ChildPath "$Domain.cert.pem"
        Write-Verbose "Writing certificate file to $File"
        Set-Content -Path $File -Value $Response.certificatechain

        $File = Join-Path -Path $Path -ChildPath "intermediate.cert.pem"
        Write-Verbose "Writing intermediate ceritificate file to $File"
        Set-Content -Path $File -Value $Response.intermediatecertificate
    }
}
