function Get-AutomationTests {
	param(
		[string]
		[Parameter(Mandatory=$True)]
		[ValidateScript({ Test-Path -Path $_ -PathType Container })]
		$Path,

		[string]
		$Profile = 'Debug',

		[string]
		$TestAttribute = 'TestFixtureAttribute',

		[string]
		$ParallelizableAttribute = 'ParallelizableAttribute'
	)

	Function Get-Types {
		param([System.Reflection.Assembly]$Assembly)
		Try {
			Return $Assembly.GetTypes()
		} Catch [System.Reflection.ReflectionTypeLoadException] {
			Return $_.Exception.Types | ?{ $_ -ne $null }
		}
	}

	Function Test-ClassHavingAttribute {
		param(
			[System.Type]$Type,
			[System.Type]$Attributes
		)

		Return $Type.IsClass -and -not($Type.IsAbstract) -and $Type.GetCustomAttributes($Attributes, $true).Count -gt 0
	}

	$RelativePath = 'KanaExpress.Automation.Tests\bin'
	$BinaryFile = 'KanaExpress.Automation.Tests.dll'
	$TestFrameworkFile = 'nunit.framework.dll'
	$TestPath = [System.IO.Path]::Combine($Path, $RelativePath, $Profile, $BinaryFile)
	$FrameworkPath = [System.IO.Path]::Combine($Path, $RelativePath, $Profile, $TestFrameworkFile)
	If (-not(Test-Path -Path $TestPath -PathType Leaf)) {
		Write-Error "Binary test file ($TestPath) not found" -ErrorAction Stop
	}
	If (-not(Test-Path -Path $FrameworkPath -PathType Leaf)) {
		Write-Error "Unit test framework file ($FrameworkPath) not found" -ErrorAction Stop
	}

	$TestAssembly = [System.Reflection.Assembly]::LoadFile($TestPath)
	$FrameworkAssembly = [System.Reflection.Assembly]::LoadFile($FrameworkPath)
	$FrameworkTypes = Get-Types -Assembly $FrameworkAssembly
	$FrameworkAttributeType = $FrameworkTypes | ?{ $_.IsClass -and -not($_.IsAbstract) -and $_.Name -eq $TestAttribute }
	$ParallelAttributeType = $FrameworkTypes | ?{ $_.IsClass -and -not($_.IsAbstract) -and $_.Name -eq $ParallelizableAttribute }
	$TestTypes = Get-Types -Assembly $TestAssembly
	$UnitTestClasses = $TestTypes | ?{ Test-ClassHavingAttribute -Type $_ -Attribute $FrameworkAttributeType }
	$ParallelClasses = $UnitTestClasses | ?{ Test-ClassHavingAttribute -Type $_ -Attribute $ParallelAttributeType }
	#$AutomationTestAttribute = $TestTypes | ?{ Test-ClassHavingAttribute -Type $_ -Attribute 'KanaExpressTestAttribute' }

	# find test classes having method test with KanaExpressTestAttribute enabled
	$ParallelClasses
}