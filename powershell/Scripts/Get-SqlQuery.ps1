function Get-SqlQuery {
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

        [int]
        $IndexResult = 0,

        [System.Data.IDbConnection]
        [ValidateNotNull()]
        [Parameter(Mandatory=$false, ParameterSetName="FromFile")]
        [Parameter(Mandatory=$false, ParameterSetName="FromString")]
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
            $ScriptContent = Get-Content -Path $FullName
            $Sql = [string]::Join("`n", $ScriptContent)
            $Command.CommandText = $Sql
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
        }
        Write-Verbose "SQL: $($Command.CommandText)"
        $SqlAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($Command)
        $DataSet = New-Object System.Data.DataSet
        $SqlAdapter.Fill($DataSet) | Out-Null
        if ($DataSet.Tables.Count -eq 0 -or $DataSet.Tables.Count -le $IndexResult) {
            Return '<EMPTY RESULT>'
        }

        return $DataSet.Tables[$IndexResult].Rows
    }
}