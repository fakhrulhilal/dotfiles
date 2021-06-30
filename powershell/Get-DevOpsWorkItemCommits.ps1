function Get-DevOpsWorkItemCommits {
	param(
		[int]
		[Parameter(Mandatory=$True)]
		$ID,

        [ValidateSet('Git', 'TFVC')]
        $Type,
		
		[string]
		$PersonalAccessToken = $global:AzureDevOpsPersonalAccessToken,
		
		[string]
		$ApiUrl = $global:AzureDevOpsApiUrl
	)
	
	Process {
		$Header = @{
            Authorization = 'Basic ' + [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes(":$PersonalAccessToken"))
        }
        $Url = "$ApiUrl/wit/workitems/$ID/?`$expand=relations&api-version=5.1"
        $TypeMatching = switch($Type) {
            Git { 'vstfs:///Git/Commit*' }
            TFVC { 'vstfs:///VersionControl*' }
        }
        $Result = Invoke-RestMethod -Method Get -Headers $Header -Uri $Url
        $Result.relations | ?{ 'ArtifactLink' -eq $_.rel -and $_.url -ilike $TypeMatching }
	}
}