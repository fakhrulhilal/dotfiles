function Register-DevOpsPipelineAgent {
	param(
		[string]
		$AgentWorkingFolder,

		[string]
		[Parameter(
			Mandatory=$True,
			HelpMessage="URL to Azure DevOps server, sample: https://dev.azure.com/MyOrg")]
		$Url,

		[string]
		[Parameter(
			Mandatory=$True,
			HelpMessage="Credential to register the agent within target machine")]
		$RegisterUsername,

		[string]
		[Parameter(Mandatory=$True)]
		$RegisterPassword,

		[string]
		[Parameter(
			Mandatory=$True,
			HelpMessage="Valid credential within Azure DevOps to connect from pipeline agent")]
		$ServiceUsername,

		[string]
		[Parameter(Mandatory=$True)]
		$ServicePassword,

		[string]
		[Parameter(Mandatory=$True)]
		$AgentPool,

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

	Function Get-WorkingFolder ($Path, $Agent) {
		$ChildFolder = [string]::Join('', [System.Linq.Enumerable]::Select($Agent.Split('-'), [Func[string, string]] { param($word) $word.Substring(0, 1) }))
		Return (Join-Path -Path $Path -ChildPath $ChildFolder)
	}

	ForEach ($Agent in $Agents) {
		$Configurator = [System.IO.Path]::Combine($InstallPath, $Agent, 'config.cmd')
		$WorkFolder = Get-WorkingFolder -Path $WorkFolder -Agent $Agent
		&$Configurator --url "$Url" `
			--auth negotiate --username "$RegisterUsername" --password "$RegisterPassword" --unattended `
			--pool $AgentPool --agent "$Agent" --work "$WorkFolder" `
			--runAsService --windowsLogonAccount "$ServiceUsername" --windowsLogonPassword "$ServicePassword"
	}
}