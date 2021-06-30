function Register-DevOpsPipelineAgent {
	param(
		[string]
		$AgentWorkingFolder,

		[string]
		[Parameter(
			Mandatory=$False,
			HelpMessage="URL to Azure DevOps server")]
		$Url = 'http://bfs-tfs-app-2.verint.corp.verintsystems.com:8080/tfs',

		[string]
		$RegisterUsername = 'VERINT\tfs-svc-acc',

		[string]
		$RegisterPassword = 'Cryptic12345!',

		[string]
		$ServiceUsername = 'VERINT\tfs-svc-acc',

		[string]
		$ServicePassword = 'Cryptic12345!',

		[string]
		$AgentPool = 'KanaExpress',

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