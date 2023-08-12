function Set-SqlParameter {
    [CmdletBinding()]
    param(
        [System.Data.IDbCommand]
        [ValidateNotNull()]
        $Command,

        [hashtable]
        [ValidateNotNull()]
        $Parameters
    )

    foreach ($Key in $Parameters.Keys) {
        $Parameter = $Command.CreateParameter()
        $Parameter.ParameterName = $Key
        $Parameter.Value = $Parameters[$Key]
        $Command.Parameters.Add($Parameter)
    }
    [void]
}