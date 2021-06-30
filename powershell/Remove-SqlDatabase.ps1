function Remove-SqlDatabase {
    [CmdletBinding()]
    param(
        [string]
        $Server, 
        
        [string]
        $Database,
        
        [string]
        $Username,
        
        [string]
        $Password
    )

    Process {
        try {
            $Connection = New-Object System.Data.SqlClient.SqlConnection
            $Connection.ConnectionString = "Data Source=$Server;Initial Catalog=master;User Id=$Username;Password=$Password;"
            Write-Verbose "Using connection string: $($Connection.ConnectionString)"
            $Connection.Open()
            $Command = $Connection.CreateCommand()
            $Command.CommandType = [System.Data.CommandType]::Text
            $Command.CommandText = "
            ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [$Database];
            "
            Write-Verbose "Executing SQL: $($Command.CommandText)"
            $Command.ExecuteNonQuery()
        }
        finally {
            $Command.Dispose()
            $Connection.Clone()
            $Connection.Dispose()
        }
    }
}