function Deploy-GitCommit {
	[CmdletBinding()]
	param(
		[string]
		[Parameter(Mandatory=$true)]
		$CommitId,

		[string]
		[Parameter(Mandatory=$true)]
		$SourceFolder,

		[string]
		[Parameter(Mandatory=$true)]
		$DestinationFolder
	)

	Begin {
		$GitCommand = Get-Command -CommandType Application -Name git -ErrorAction SilentlyContinue
		if ($null -eq $GitCommand) {
			Write-Error "Git command not found"
		}

		$SourceFolder = (Get-Item $SourceFolder).FullName.Trim('\')
		$DestinationFolder = (Get-Item $DestinationFolder).FullName.Trim('\')
		$BaseDirectory = (Get-Location).Path.Trim('\')
		Write-Verbose "Deploying from '$SourceFolder' to '$DestinationFolder'"
		if ([string]::IsNullOrWhiteSpace($SourceFolder) -or [string]::IsNullOrWhiteSpace($DestinationFolder)) {
			Write-Error "Source/destination folder can't be empty'"
		}
	}

	Process {
		$Files = &$GitCommand show --name-only --oneline --pretty="format:" $CommitId
		if ($null -eq $Files -or $Files.Length -eq 0) {
			Write-Warning "No files found in git commit"
		}
		foreach ($File in $Files) {
			$RelativeDirectory = (Get-Item -Path $File).Directory.FullName.Replace($BaseDirectory, [string]::Empty).TrimStart('\')
			$TargetFolder = $DestinationFolder
			$SourcePath = Join-Path -Path $SourceFolder -ChildPath $File
			if (-not([string]::IsNullOrWhiteSpace($RelativeDirectory))) {
				$TargetFolder = Join-Path $DestinationFolder -ChildPath $RelativeDirectory
				if (-not(Test-Path -PathType Container -Path $TargetFolder)) {
					Write-Verbose "Creating $TargetFolder"
					New-Item -ItemType Directory -Path $TargetFolder | Out-Null
				}
			} 
			Write-Verbose "Copying '$SourcePath' to '$TargetFolder'"
			Copy-Item -Path $SourcePath -Destination $TargetFolder | Out-Null
		}
	}
}