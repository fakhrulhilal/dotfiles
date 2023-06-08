function Get-Hash {
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [ValidateNotNullOrEmpty()]
        $Text,

        [ValidateNotNullOrEmpty()]
        [ValidateSet("MD5", "SHA1", "SHA256", "SHA384", "SHA512")]
        [Parameter(Mandatory=$true)]
        $Algo = 'Text'
    )

    Begin {
        $Encryptor = switch ($Algo) {
            "MD5" { [System.Security.Cryptography.MD5]::Create() }
            "SHA1" { [System.Security.Cryptography.SHA1]::Create() }
            "SHA256" { [System.Security.Cryptography.SHA256]::Create() }
            "SHA384" { [System.Security.Cryptography.SHA384]::Create() }
            "SHA512" { [System.Security.Cryptography.SHA512]::Create() }
        }
    }

    Process {
        $TextBytes = [System.Text.Encoding]::ASCII.GetBytes($Text)
        $CipherBytes = $Encryptor.ComputeHash($TextBytes)
        $Cipher = [System.Convert]::ToBase64String($CipherBytes)
        return $Cipher
    }

    End {
        $Encryptor.Dispose()
    }
}