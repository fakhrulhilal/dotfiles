function Deploy-GitCommit {
	[CmdletBinding()]
	param(
		[string]
		[Parameter(Mandatory=$true)]
		$CommitId,

		[string]
		[Parameter(Mandatory=$true)]
		$DestinationFolder,

		[string]
		[Parameter(Mandatory=$false)]
		$SourceFolder = (Get-Location).Path,

		[string]
		[Parameter(Mandatory=$false)]
		$BackupFolder,

		[switch]
		$Simulate,

		[switch]
		$BackupOnly
	)

	Begin {
		$GitCommand = Get-Command -CommandType Application -Name git -ErrorAction SilentlyContinue
		if ($null -eq $GitCommand) {
			Write-Error "Git command not found"
		}

		$OldBaseDirectory = (Get-Location).Path
		$OldGitBranch = Get-GitCurrentBranch
		$SourceFolder = (Get-Item $SourceFolder).FullName.Trim('\')
		Set-Location $SourceFolder
		$DestinationFolder = (Get-Item $DestinationFolder).FullName.Trim('\')
		Write-Verbose "Deploying from '$SourceFolder' to '$DestinationFolder'"
		if ([string]::IsNullOrWhiteSpace($SourceFolder) -or [string]::IsNullOrWhiteSpace($DestinationFolder)) {
			Write-Error "Source/destination folder can't be empty'"
		}

		Write-Verbose "Switching to git commit $CommitId from branch $OldGitBranch"
		if (-not $Simulate) {
			git stash | Out-Null
			git checkout $CommitId | Out-Null
		}
	}

	Process {
		$Files = &$GitCommand show --name-only --oneline --pretty="format:" $CommitId
		if ($null -eq $Files -or $Files.Length -eq 0) {
			Write-Warning "No files found in git commit"
		} else {
			$InlineFiles = Join-String -InputObject $Files -Separator "`n- "
			Write-Verbose "For the following files:`n- $InlineFiles"
		}

		if (-not([string]::IsNullOrWhiteSpace($BackupFolder))) {
			if (-not(Test-Path -Path $BackupFolder -PathType Container) -and -not $Simulate) {
				New-Item -Path $BackupFolder -ItemType Directory -Force | Out-Null
			}
			if (Test-Path -Path $BackupFolder -PathType Container) {
				$BackupFolder = (Get-Item $BackupFolder).FullName.Trim('\')
			}
			$PreFolder = Join-Path $BackupFolder -ChildPath 'pre'
			Write-Verbose "Creating backup pre folder: $PreFolder"
			if (-not $Simulate) {
				New-Item -ItemType Directory -Path $PreFolder -Force | Out-Null
			}
			Copy-FilesInner -Files $Files -BaseDirectory $SourceFolder -Simulate:$Simulate `
				-DestinationFolder $PreFolder -SourceFolder $DestinationFolder
			$PostFolder = Join-Path $BackupFolder -ChildPath 'update'
			Write-Verbose "Creating backup post folder: $PostFolder"
			if (-not $Simulate) {
				New-Item -ItemType Directory -Path $PostFolder -Force | Out-Null
			}
			Copy-FilesInner -Files $Files -BaseDirectory $SourceFolder -Simulate:$Simulate `
				-DestinationFolder $PostFolder -SourceFolder $SourceFolder
		}

		if (-not($BackupOnly)) {
			Copy-FilesInner -Files $Files -BaseDirectory $SourceFolder -Simulate:$Simulate `
				-DestinationFolder $DestinationFolder -SourceFolder $SourceFolder
		}
	}

	End {
		if (-not $Simulate) {
			git checkout $OldGitBranch | Out-Null
			git stash pop | Out-Null
		}
		Set-Location $OldBaseDirectory
	}
}

function Get-GitCurrentBranch {
	git branch | ?{ $_ -match '^\*' } | %{ $_.Trim('*').Trim() }
}

function Copy-FilesInner {
	[CmdletBinding()]
	param(
		[string[]]$Files,
		[string]$BaseDirectory,
		[string]$DestinationFolder,
		[string]$SourceFolder,
		[switch]$Simulate
	)

	Process {
		foreach ($File in $Files) {
			$Item = Get-Item -Path $File -ErrorAction SilentlyContinue
			$RelativeDirectory = $null -ne $Item ? $Item.Directory.FullName.Replace($BaseDirectory, [string]::Empty).TrimStart('\') : $File.Replace('/', '\')
			$TargetFolder = $DestinationFolder
			$SourcePath = Join-Path -Path $SourceFolder -ChildPath $File
			if (-not([string]::IsNullOrWhiteSpace($RelativeDirectory))) {
				$TargetFolder = Join-Path $DestinationFolder -ChildPath $RelativeDirectory
				if (-not(Test-Path -PathType Container -Path $TargetFolder)) {
					Write-Verbose "Creating folder: $TargetFolder"
					if (-not $Simulate) { 
						New-Item -ItemType Directory -Path $TargetFolder | Out-Null
					}
				}
			} 
			if (Test-Path -Path $SourcePath -PathType Leaf) {
				Write-Verbose "Copying '$SourcePath' to '$TargetFolder'"
				if (-not $Simulate) {
					Copy-Item -Path $SourcePath -Destination $TargetFolder | Out-Null
				}
			} else {
				Write-Warning "The file '$SourcePath' doesn't exist, skipping"
			}
		}
	}
}