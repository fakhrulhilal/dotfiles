Function Get-ExpirableVerintExpressMembers {
	param(
		[ValidateNotNull()]
		[System.Management.Automation.PSCredential]
		[System.Management.Automation.Credential()]
		$Credential
	)
	$Members = Get-ADGroupMember -Credential $Credential -Identity Verint.Express | Select-Object -ExpandProperty SamAccountName
	$ExpirableUsers = Get-ADUser -SearchBase "CN=Users,DC=kana-test,DC=com" -Filter { Enabled -eq $True -and PasswordNeverExpires -eq $False } -Properties "DisplayName", "msDS-UserPasswordExpiryTimeComputed"
	Return $ExpirableUsers | ? { $Members.Contains($_.SamAccountName) } 
}

Function Update-ADUserPasswordExpirity {
	param(
		[parameter(Mandatory = $true, ValueFromPipeline = $true)]
		$User,

		[ValidateNotNull()]
		[System.Management.Automation.PSCredential]
		[System.Management.Automation.Credential()]
		$Credential
	)
	Process {
		Set-ADUser -Identity $User -Credential $AdminCredential -Replace @{pwdLastSet = 0 }
		Set-ADUser -Identity $User -Credential $AdminCredential -Replace @{pwdLastSet = -1 }
	}
}

Function Test-ADUserExpirityPassword {
	param(
		[parameter(Mandatory = $true, ValueFromPipeline = $true)]
		$User
	)
	Begin {
		$Today = [datetime]::Now.Date
	}
	Process {
		Return ($Today -ge ([datetime]::FromFileTime($_."msDS-UserPasswordExpiryTimeComputed")).Date)
	}
}

<#
$AdminCredential = New-Object System.Management.Automation.PSCredential ("KANA-TEST\kxadmin", (ConvertTo-SecureString 'K@n1Express' -AsPlainText -Force))

Get-ExpirableVerintExpressMembers -Credential $AdminCredential | Test-ADUserExpirityPassword | Update-ADUserPasswordExpirity -Credential $AdminCredential
Get-ExpirableVerintExpressMembers -Credential $AdminCredential | select -Property DisplayName, SamAccountName, DistinguishedName, @{Name = "ExpiryDate"; Expression = { [datetime]::FromFileTime($_."msDS-UserPasswordExpiryTimeComputed") } }
#>