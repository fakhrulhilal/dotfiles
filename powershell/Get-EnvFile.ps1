function Get-EnvFile {
    [CmdletBinding()]
	param(
		[string]
		[ValidateNotNullOrEmpty()]
		[validateScript({ Test-Path -PathType Leaf -Path $_ }, ErrorMessage = 'Path must be valid file')]
		$Path,

		[string]
		[ValidateSet('User', 'Process', 'Script')]
		$Scope = 'Script'
	)

	Process {
		Get-Content $Path | ForEach-Object {
			# if line is empty or starts with #, skip it
			$line = $_.Trim()
			if ([string]::IsNullOrWhiteSpace($line) -or $line -match '^\s*#') {
				# skip comments and empty lines
				return
			}

			$name, $value = $_.split('=')
			if ([string]::IsNullOrWhiteSpace($name) -or $name.Trim() -match '^\s*#') {
				return
			}
			
			# remove comments from value
			# find index of fist # in value but not if its surrounded by double quotes
			$matches = [regex]::Matches($value, '(?<!")#(?!")')

			# Filter out matches that are actually within double quotes
			$filteredMatches = $matches | Where-Object {
				$beforeMatch = $value.Substring(0, $_.Index)
				$afterMatch = $value.Substring($_.Index + 1)
				
				# Ensure the match is not within double quotes
				($beforeMatch -split '"').Count % 2 -eq 1 -and ($afterMatch -split '"').Count % 2 -eq 1
			}

			# If matches are found, get the first match index
			if ($filteredMatches.Count -gt 0) {
				$firstMatch = $filteredMatches[0]
				$firstIndex = $firstMatch.Index
				$value = $value.Substring(0, $firstIndex)
			}

			# trim and remove double quotes from value
			$value = $value.Trim().Trim('"')
			Write-Verbose "Got name = $name, with value = $value"
			
			# choose where to store the environment variable
			switch ($Scope) {
				"Process" {
					[Environment]::SetEnvironmentVariable($name, $value, 0) # 0 = [System.EnvironmentVariableTarget]::Process
				}
				"User" {
					[Environment]::SetEnvironmentVariable($name, $value, 1) # 1 = [System.EnvironmentVariableTarget]::User
				}
				# default to script
				default {
					Set-Variable -Name $name -Value $value -Scope Script
				}
			}
		}
	}
}
