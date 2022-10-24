<#
.SYNOPSIS
    Clears the internal instance of PorkBun API client
.DESCRIPTION
    Clears the internal PorkBun API client resources.  The instance will be reinstantiated in other module calls.
.EXAMPLE
    Clear-PorkBun
.NOTES
    Should only be used for testing
#>
function Clear-Resources {
    if ($Script:PorkBunConnection) {
        Remove-Variable -Name PorkBunConnection -Scope script
    }
}
