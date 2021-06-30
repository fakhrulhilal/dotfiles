function Install-DevOpsPipelineAgent {
	param(
		[string]
		[Parameter(
			Mandatory=$False,
			HelpMessage="Path to azure pipeline agent .zip file")]
		$PipelineAgentMaster,

		[string]
		[Parameter(
			Mandatory=$False,
			HelpMessage="Path to install azure pipeline agents")]
		$InstallPath = (Convert-Path .),

		[string[]]
		[Parameter(
			Mandatory=$False,
			HelpMessage="List of agent to be installed")]
	   $Agents = @('agent-1', 'agent-2', 'agent-3', 'agent-4', 'agent-5', 'release-interactive')
	)

	If ($null -eq $PipelineAgentMaster) {
		$PipelineAgentMaster = Get-Item -Filter *.zip
	}
	If ($null -eq $PipelineAgentMaster) {
		Write-Error "No pipeline agent master found" -ErrorAction Stop
	}

	$PipelineAgentFilename = ((Get-Item $PipelineAgentMaster).BaseName)
	$PipelineAgentSourceFolder = Join-Path -Path $InstallPath -ChildPath $PipelineAgentFilename

	Add-Type -Assembly System.IO.Compression.FileSystem
	[System.IO.Compression.ZipFile]::ExtractToDirectory($PipelineAgentMaster, $PipelineAgentSourceFolder)

	Function Install-Agent {
		param(
			[string]
			$Path,

			[string]
			$Source,

			[string]
			$Agent
		)

		Process {
			$SourcePath = Join-Path -Path $Path -ChildPath $Source
			$AgentFiles = Get-ChildItem $SourcePath -File
			$AgentFolders = Get-ChildItem $SourcePath -Directory

			$DestinationFolder = Join-Path -Path $Path -ChildPath $Agent
			ForEach ($Folder in $AgentFolders) {
				New-Item -ItemType Junction -Path (Join-Path -Path $DestinationFolder -ChildPath $Folder) -Value (Join-Path -Path $SourcePath -ChildPath $Folder)
			}
			ForEach ($File in $AgentFiles) {
				New-Item -ItemType SymbolicLink -Path (Join-Path -Path $DestinationFolder -ChildPath $File) -Value (Join-Path -Path $SourcePath -ChildPath $File)
			}
		}
	}

	ForEach ($Agent in $Agents) {
		Install-Agent -Path $InstallPath -Source $PipelineAgentFilename -Agent $Agent
	}
}