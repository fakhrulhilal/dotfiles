Function Set-ItemSecurity {
<#
.SYNOPSIS
    A shortcut to manage file/folder security permission

.PARAMETER Path
    Path to file/folder to be set for its security permission

.PARAMETER Account
    User account

.PARAMETER Security
    Access right
#>
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$True, ValueFromPipeline=$True)]
        [ValidateNotNullOrEmpty()]
        $Path,

        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$True)]
        $Account,

        [ValidateSet("Read","Modify","ReadAndExecute","None")]
        [Parameter(Mandatory=$True)]
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

    $null
}