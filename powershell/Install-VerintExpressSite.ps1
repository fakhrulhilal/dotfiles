function Install-VerintExpressSite {
	<#     
	.SYNOPSIS     
		A script to set new Verint Express site using source solution folder
		 
	.DESCRIPTION   
		A installer script to deploy Verint Express product package. 
		The steps are written step by step based on installation manual.
		The installer may differ for each product version. See note for covered product version.
		This script must be run with an account with administrative privilege.

	.PARAMETER WebsiteName
		Website name

	.PARAMETER ProjectPath
		Path to Verint Express solution folder

	.PARAMETER WebsiteAppPoolUsername
		A user account in web server that will be used for website app pool login. Include the domain name when necessary.

	.PARAMETER WebsiteAppPoolPassword
		A user account's password in web server that will be used for website app pool login
	#>

	[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingCmdletAliases', '',Justification = "Used aliases are common version ones")]
	[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingPlainTextForPassword', '', Justification = "Need plain value to be passed to another process")]
	param
	(
		[Parameter(
			Mandatory=$True,
			HelpMessage="Website name")]
		[string]
		$WebsiteName,

		[Parameter(
			Mandatory=$True,
			HelpMessage="Path to Verint Express solution folder")]
		[string]
		$ProjectPath,

		[Parameter(
			Mandatory=$True,
			HelpMessage="A user account in web server that will be used for website app pool login,  include the domain name when necessary")]
		$WebsiteAppPoolUsername,

		[Parameter(
			Mandatory=$True,
			HelpMessage="A user account's password in web server that will be used for website app pool login")]
		$WebsiteAppPoolPassword,

		[Parameter(
			Mandatory=$False,
			HelpMessage="Set custom path for drop folder instead of searching folder named 'drop' in the project path"
		)]
		[ValidateNotNullOrEmpty()]
		$DropFolderPath,

		[Parameter(Mandatory=$False)]
		[switch]
		$SetSecurity = $False
	)

	If (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
		Write-Error "This script need to be run as Administrator" -ErrorAction Stop
	}

	Write-Host " __      __       _       _     ______                             "
	Write-Host " \ \    / /      (_)     | |   |  ____|         -Package Installer-"
	Write-Host "  \ \  / ___ _ __ _ _ __ | |_  | |__  __  ___ __  _ __ ___ ___ ___ "
	Write-Host "   \ \/ / _ | '__| | '_ \| __| |  __| \ \/ | '_ \| '__/ _ / __/ __|"
	Write-Host "    \  |  __| |  | | | | | |_  | |____ >  <| |_) | | |  __\__ \__ \"
	Write-Host "     \/ \___|_|  |_|_| |_|\__| |______/_/\_| .__/|_|  \___|___|___/"
	Write-Host "                                           | |                     "
	Write-Host "                                           |_|                     "
	Write-Host ""
	 
	Import-Module WebAdministration
	$ProjectPath = (Get-Item $ProjectPath).FullName
	# STEP 6.1.3 Create Application Pools
	Write-Host "\-- Creating website app pools"
	Function New-WebsiteAppPool {
		param(
			[string]$Name,
			[string]$Enable32Bit,
			[string]$LoadUserProfile,
			[switch]$AutoStart,
			[Parameter(Mandatory=$False)][ValidateSet("AlwaysRunning","OnDemand")]$StartMode)
		If ($PSBoundParameters['Verbose']) {
			Write-Host "|   |-- $Name"
		}
		If (-not (Test-Path -Path IIS:\AppPools\$Name)) {
			New-Item IIS:\AppPools\$name -ErrorAction Stop | Out-Null
		}
		$integratedPipelineMode = 0
		Set-ItemProperty -Path IIS:\AppPools\$Name -Name managedRuntimeVersion -Value 'v4.0' | Out-Null
		Set-ItemProperty -Path IIS:\AppPools\$Name -Name managedPipelineMode -Value $integratedPipelineMode | Out-Null
		Set-ItemProperty -Path IIS:\AppPools\$Name -Name processModel -Value @{
			userName = "$WebsiteAppPoolUsername";
			password = "$WebsiteAppPoolPassword";
			identityType = 3;
			loadUserProfile = $LoadUserProfile
		} -ErrorAction Stop | Out-Null
		Set-ItemProperty -Path IIS:\AppPools\$Name -Name enable32BitAppOnWin64 -Value $Enable32Bit | Out-Null
		If ($AutoStart) {
			Set-ItemProperty -Path IIS:\AppPools\$Name -Name autoStart -Value True | Out-Null
		}
		If (-not([string]::IsNullOrWhiteSpace($StartMode))) {
			$Mode = If ('AlwaysRunning' -eq $StartMode) { 1 } Else { 0 }
			Set-ItemProperty -Path IIS:\AppPools\$Name -Name startMode -Value $Mode | Out-Null
			If (1 -eq $Mode) {
				Set-ItemProperty -Path IIS:\AppPools\$Name -Name processModel.idleTimeout -Value ([timespan]::FromMinutes(0)) | Out-Null
			}
		}
	}
	New-WebsiteAppPool -Name $WebsiteName-public -LoadUserProfile "True" -Enable32Bit "False" -Verbose
	New-WebsiteAppPool -Name $WebsiteName-app -LoadUserProfile "True" -Enable32Bit "False" -AutoStart -StartMode AlwaysRunning -Verbose
	New-WebsiteAppPool -Name $WebsiteName-hypersearch -LoadUserProfile "False" -Enable32Bit "True" -Verbose

	# STEP 6.1.4 Create Websites
	Write-Host "|-- Creating website $($WebsiteName)"
	If (-not (Test-Path -Path IIS:\Sites\$WebsiteName)) {
		New-Item IIS:\Sites\$WebsiteName -ErrorAction Stop `
			-PhysicalPath (Join-Path -Path $ProjectPath -ChildPath Front) `
			-Bindings @{
				protocol = "http";
				bindingInformation = "*:80:$WebsiteName"
			} | Out-Null
	}
	Set-ItemProperty IIS:\Sites\$WebsiteName -Name applicationPool -Value $WebsiteName-public -ErrorAction Stop | Out-Null
	# STEP 6.1.6 Create Website Applications
	Function New-WebApplication {
		[CmdletBinding()]
		param(
			[string]$AppName,
			[string]$Path,
			[string]$AppPool,
			[string]$Description
		)

		If ($PSBoundParameters['Verbose']) {
			If ([string]::IsNullOrWhiteSpace($Description)) {
				$Description = $AppName
			}
			Write-Host "|   |-- $Description"
		}
		New-Item IIS:\Sites\$WebsiteName\$AppName -PhysicalPath (Join-Path -Path $ProjectPath -ChildPath $Path) -ItemType Application -ApplicationPool "$($WebsiteName)-$($AppPool)" | Out-Null
	}
	Write-Host "\-- Creating website applications"
	New-WebApplication -AppName 'API' -Path 'Kana.Express.Api' -AppPool 'app' -Verbose
	New-WebApplication -AppName 'APIAdmin' -Path 'VerintExpress.Api.Administration' -AppPool 'app' -Verbose
	New-WebApplication -AppName 'APIGeneral' -Path 'VerintExpress.Api.General' -AppPool 'app' -Verbose
	Set-ItemProperty IIS:\Sites\$WebsiteName\APIGeneral -Name preloadEnabled -Value 1
	New-WebApplication -AppName 'ChatBot' -Path 'ChatBot' -AppPool 'public' -Verbose
	New-WebApplication -AppName 'HyperSearchWS' -Path 'HyperSearchWS' -AppPool 'hypersearch' -Verbose
	New-WebApplication -AppName 'Login' -Path 'Kana.Express.Agent' -AppPool 'app' -Verbose
	New-WebApplication -AppName 'SiteManager' -Path 'SiteManager' -AppPool 'app' -Verbose
	New-WebApplication -AppName 'SiteManager\T5WebControls' -Path 'SiteManager\T5WebControls' -AppPool 'app' -Description 'T5WebControls within SiteManager' -Verbose
	New-WebApplication -AppName 'SpellChecker' -Path 'SpellChecker' -AppPool 'app' -Verbose
	#New-WebApplication -AppName 'Scheduling' -Path 'Scheduling' -AppPool 'app' -Verbose
	New-WebApplication -AppName 'WebService' -Path 'WebService' -AppPool 'public' -Verbose
	New-WebApplication -AppName 'XServer' -Path 'X-Server\Trinicom.WebServiceApp' -AppPool 'app' -Verbose
	If (Test-Path (Join-Path -Path $ProjectPath -ChildPath 'X-Server\Trinicom.WebApp')) {
		New-WebApplication -AppName 'XServerTest' -Path 'X-Server\Trinicom.WebApp' -AppPool 'app' -Verbose
	}

	# STEP 6.1.7 Add Virtual Directories
	Write-Host "\-- Add Virtual Directories"
	Function New-WebVirtualDirectory {
		[CmdletBinding()]
		param(
			[string]$Path,
			[string]$Name
		)
		
		If ([string]::IsNullOrWhiteSpace($Name)) {
			$Name = $Path
		}
		If ($PSBoundParameters['Verbose']) {
			Write-Host "|   |-- $Name"    
		}
		New-Item IIS:\Sites\$WebsiteName\$Name -ItemType VirtualDirectory -PhysicalPath (Join-Path -Path $ProjectPath -ChildPath $Path) | Out-Null
	}
	New-WebVirtualDirectory -Path 'PrivateData' -Verbose
	If ([string]::IsNullOrWhiteSpace($DropFolderPath)) {
		$DropFolderPath = Join-Path -Path $ProjectPath -ChildPath 'Drop'
	}
	If (Test-Path -Path $DropFolderPath) {
		New-WebVirtualDirectory -Path $DropFolderPath -Name 'MailImport' -Verbose
	}

	If ($SetSecurity) {
		# STEP 6.1.8 Set folder permissions
		Function Set-Security {
			[CmdletBinding()]
			param(
				[string]$Path,
				[string]$Account,
				[ValidateSet("Read","Modify","ReadAndExecute","None")]
				$Security
			)
			$RightDescription = [string]::Empty
			If ([string]::IsNullOrWhiteSpace($Path)) {
				$FullPath = $ProjectPath
			} Else {
				$FullPath = Join-Path -Path $ProjectPath -ChildPath $Path
			}
			If ($Security -eq "None") {
				$acl = Get-Acl -Path "$FullPath"
				$ace = $acl.Access | where IdentityReference -EQ $Account
				If ($null -ne $ace) {
					$acl.RemoveAccessRule($ace) | Out-Null
					If ($PSBoundParameters['Verbose']) {
						Write-Host "|   |-- Revoke from $Account for $FullPath"
					}
					Set-Acl -Path "$FullPath" -AclObject $acl | Out-Null
				}
			}
			Else {
				switch ($Security) {
					"Read" {
						$rights = [System.Security.AccessControl.FileSystemRights]"Read,ListDirectory"
						$RightDescription = 'Read'
					}
					"Modify" {
						$rights = [System.Security.AccessControl.FileSystemRights]"Modify,ListDirectory"
						$RightDescription = 'Read & Modify'
					}
					"ReadAndExecute" {
						$rights = [System.Security.AccessControl.FileSystemRights]"ReadAndExecute,ListDirectory"
						$RightDescription = 'Read & Execute'
					}
				}
				$user = New-Object System.Security.Principal.NTAccount($Account)
				$propagation = [System.Security.AccessControl.PropagationFlags]::None
				$inherit = [System.Security.AccessControl.InheritanceFlags]"ContainerInherit,ObjectInherit"
				$accessType = [System.Security.AccessControl.AccessControlType]::Allow
				$ace = New-Object System.Security.AccessControl.FileSystemAccessRule($user, $rights, $inherit, $propagation, $accessType)
				$acl = Get-Acl -Path "$FullPath"
				$acl.AddAccessRule($ace)
				If ($PSBoundParameters['Verbose']) {
					Write-Host "|   |-- Grant $RightDescription to $Account for $FullPath"
				}
				Set-Acl -Path "$FullPath" -AclObject $acl | Out-Null
			}
		}
		Write-Host "\-- Set folder permissions"
		Set-Security -Path "Kana.Express.Agent" -Account "BUILTIN\IIS_IUSRS" -Security Read -Verbose
		Set-Security -Path "Kana.Express.Agent" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "Kana.Express.Api" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
		Set-Security -Path "Kana.Express.Api" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "VerintExpress.Api.Administration" -Account "BUILTIN\IIS_IUSRS" -Security Read -Verbose
		Set-Security -Path "VerintExpress.Api.Administration" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "VerintExpress.Api.General" -Account "BUILTIN\IIS_IUSRS" -Security Read -Verbose
		Set-Security -Path "VerintExpress.Api.General" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "VerintExpress.Api.General\App_Data" -Account "BUILTIN\IIS_IUSRS" -Security Modify -Verbose
		Set-Security -Path "VerintExpress.Api.General\App_Data" -Account "$WebsiteAppPoolUsername" -Security Modify -Verbose
		If (Test-Path -Path $DropFolderPath -PathType Container) {
			Set-Security -Path $DropFolderPath -Account "BUILTIN\IIS_IUSRS" -Security Modify -Verbose
			Set-Security -Path $DropFolderPath -Account "$WebsiteAppPoolUsername" -Security Modify -Verbose
		}
		Set-Security -Account "BUILTIN\IIS_IUSRS" -Security Read -Verbose
		Set-Security -Account "$WebsiteAppPoolUsername" -Security Read -Verbose
		Set-Security -Path "PrivateData" -Account "BUILTIN\IIS_IUSRS" -Security Modify -Verbose
		Set-Security -Path "PrivateData" -Account "$WebsiteAppPoolUsername" -Security Modify -Verbose
		If (Test-Path -Path "Scheduling" -PathType Container) {
			Set-Security -Path "Scheduling" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
			Set-Security -Path "Scheduling" -Account "$WebsiteAppPoolUsername" -Security Modify -Verbose
		}
		Set-Security -Path "SiteManager" -Account "BUILTIN\IIS_IUSRS" -Security None -Verbose
		Set-Security -Path "SiteManager" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "SiteManager\T5WebControls" -Account "BUILTIN\IIS_IUSRS" -Security None -Verbose
		Set-Security -Path "SiteManager\T5WebControls" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "SpellChecker" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
		Set-Security -Path "SpellChecker" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "WebService" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
		Set-Security -Path "WebService" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		Set-Security -Path "X-Server\Trinicom.WebServiceApp" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
		Set-Security -Path "X-Server\Trinicom.WebServiceApp" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		If (Test-Path -Path "X-Server\Trinicom.WebApp") {
			Set-Security -Path "X-Server\Trinicom.WebApp" -Account "BUILTIN\IIS_IUSRS" -Security ReadAndExecute -Verbose
			Set-Security -Path "X-Server\Trinicom.WebApp" -Account "$WebsiteAppPoolUsername" -Security ReadAndExecute -Verbose
		}
		If (Test-Path -Path "PickUp" -PathType Container) {
			Set-Security -Path "PickUp" -Account "$WebsiteAppPoolUsername" -Security Modify -Verbose
		}
	}
}