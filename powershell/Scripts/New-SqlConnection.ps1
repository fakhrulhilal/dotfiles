function New-SqlConnection {
    [CmdletBinding()]
    param(
        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$false)]
        $Server = '(local)',

        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$false, Position=0)]
        $Database = 'master',

        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$false)]
        $Username,

        [string]
        [ValidateNotNullOrEmpty()]
        [Parameter(Mandatory=$false)]
        $Password
    )

    Process {
        Try {
            $Connection = New-Object System.Data.SqlClient.SqlConnection
            $Connection.ConnectionString = "Data Source=$Server;Initial Catalog=$Database;"
            If (-not([string]::IsNullOrEmpty($Username)) -and -not([string]::IsNullOrEmpty($Password))) {
                $Connection.ConnectionString += "User Id=$Username;Password=$Password;"
            } Else {
                $Connection.ConnectionString += "Integrated Security=true;"
            }
            Write-Verbose "Using connection string: $($Connection.ConnectionString)"
            $Connection.Open()
            $Script:SqlConnection = $Connection
            return $Connection
        }
        Catch [System.Data.SqlClient.SqlException] {
            Write-Host "Query execution error with connection string $($Connection.ConnectionString)"
            Write-Host "Error message in main: $($_.Exception.Message)"
            Write-Host "Inner exception in main: $($_.Exception.InnerException)"
        }
    }
}