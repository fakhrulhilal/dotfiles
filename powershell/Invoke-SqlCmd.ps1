function Invoke-SqlCmd {
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$true, Position=0, ParameterSetName="FromString", ValueFromPipeline=$true)]
        [ValidateNotNullOrEmpty()]
        $Query,

        [ValidateNotNullOrEmpty()]
        [ValidateSet("StoredProcedure", "Text")]
        [Parameter(Mandatory=$false, ParameterSetName="FromString")]
        $QueryType = 'Text',

        [string]
        [Parameter(Mandatory=$true, ParameterSetName="FromFile", ValueFromPipelineByPropertyName=$true)]
        [Alias('ScriptFile', 'Path')]
        [ValidateScript({ Test-Path -Path $_ -PathType Leaf })]
        $FullName,

        [hashtable]
        [Parameter(Mandatory=$false)]
        [ValidateNotNull()]
        $Parameters,

        [System.Data.IDbConnection]
        [ValidateNotNull()]
        [Parameter(Mandatory=$false, ParameterSetName="FromString")]
        [Parameter(Mandatory=$false, ParameterSetName="FromFile")]
        $Connection = $Script:SqlConnection
    )

    Process {
        if (-not($PSBoundParameters.ContainsKey('Connection')) -and $null -eq $Connection) {
            $Connection = New-SqlConnection -Server $Server -Database $Database -Username $Username -Password $Password
        }
        $Command = $Connection.CreateCommand()
        if ($PSBoundParameters.ContainsKey('Parameters')) {
            Set-SqlParameter -Parameters $Parameters -Command $Command | Out-Null
        }

        If (-not([string]::IsNullOrEmpty($FullName))) {
            $Command.CommandType = [System.Data.CommandType]::Text
            $Content = [System.IO.File]::ReadAllText((Get-Item $FullName).FullName)
            if ([string]::IsNullOrWhiteSpace($Content)) {
                Write-Error "Script file ($FullName) has no content in it"
            }
            $Splitted = @($Content -split '((?:\r?\n)+|\W|^)GO((?:\r?\n)+|\W\$)')
            $Transaction = $Connection.BeginTransaction()
            try {
                $Counter = 1
                foreach ($Sql in $Splitted) {
                    if ([string]::IsNullOrWhiteSpace($Sql)) {
                        continue
                    }
                    $Command.Transaction = $Transaction
                    $Command.CommandText = $Sql
                    Write-Verbose "SQL#$($Counter): $($Command.CommandText)`n"
                    $Command.ExecuteNonQuery()
                    $Counter++
                }
                $Transaction.Commit()
            }
            finally {
                $Command.Dispose()
                $Transaction.Dispose()
            }
        } Else {
            $Command.CommandText = $Query
            switch ($QueryType) {
                "StoredProcedure" {
                    $Command.CommandType = [System.Data.CommandType]::StoredProcedure
                }
                "Text" {
                    $Command.CommandType = [System.Data.CommandType]::Text
                }
            }
            Write-Verbose "SQL: $($Command.CommandText)"
            try {
                $Command.ExecuteNonQuery()
            }
            finally {
                $Command.Dispose()
            }
        }
    }
}
