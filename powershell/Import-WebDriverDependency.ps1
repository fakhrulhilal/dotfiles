function Import-WebDriverDependency {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$false)]
        [string]
        $Path
    )

    [System.Collections.ArrayList]$LibraryFolders = $Env:ProgramFiles, ${Env:ProgramFiles(x86)} | Join-Path -ChildPath 'Verint\Regression Automation Framework\lib\Products'
    if (-not([string]::IsNullOrEmpty($Path))) {
        # load RAF dir at first priority than other
        $LibraryFolders.Insert(0, $Path)
    }
    $LibraryFiles = 'WebDriver.dll', 'WebDriver.Support.dll', 'UIAutomationTypes.dll', 'UIAutomationClient.dll', `
        'ThoughtWorks.Selenium.Core.dll', 'Selenium.WebDriverBackedSelenium.dll'
    $LibraryFiles | %{ Join-Path -Path $LibraryFolders -ChildPath $_ } | ?{ Test-Path -Path $_ -PathType Leaf } | %{ Add-Type -Path $_ }
}