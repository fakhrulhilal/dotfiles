$Script:PorkBunConnection = $null

# Handles the removal of the module
$ExecutionContext.SessionState.Module.OnRemove =
{
    Clear-Resources
}.GetNewClosure()

# Classes used in Polaris.Class.Ps1 need to be loaded before it
$Classes = @( Get-ChildItem -Path $PSScriptRoot\lib -Filter *.class.ps1 -ErrorAction SilentlyContinue)

# Get public and private function definition files.
$Public = @( Get-ChildItem -Path $PSScriptRoot\Public -Filter *.ps1 -Recurse -ErrorAction SilentlyContinue )
$Private = @( Get-ChildItem -Path $PSScriptRoot\Private -Filter *.ps1 -Recurse -ErrorAction SilentlyContinue )

# Dot source the files
Foreach ($import in @($Public + $Private + $Classes)) {
    Try {
        . $import.fullname
    }
    Catch {
        Write-Error -Message "Failed to import function $($import.fullname): $_"
    }
}

Export-ModuleMember -Function Clear-Resources
# Read in or create an initial config file and variable
# Export Public functions ($Public.BaseName) for WIP modules
# Set variables visible to the module and its functions only

Export-ModuleMember -Function $Public.Basename
