function Close-SqlConnection {
    [CmdletBinding()]
    param(
        [System.Data.IDbConnection]
        [ValidateNotNull()]
        [Parameter(Mandatory=$false)]
        $Connection = $Script:SqlConnection
    )

    Process {
        if ($null -ne $Connection) {
            if ($Connection.State -eq [System.Data.ConnectionState]::Open) {
                $Connection.Close()
            }

            $Connection.Dispose()
        }
    }
}