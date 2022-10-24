<#
.SYNOPSIS
    Remove properties from certain object
#>
function Remove-Property {
    param (
        [PSCustomObject]
        [Parameter(Mandatory=$True, ValueFromPipeline=$True)]
        [ValidateNotNull()]
        $InputObject,

        [string[]]
        $Names
    )
    
    process { 
        $Hash = @{}
        foreach ($Property in $InputObject.psobject.properties) {
            if (-not($Names -contains $Property.Name)) {
                $Hash[$Property.Name] = $Property.Value
            }
        }

        New-Object psobject -Property $Hash
    }
}
