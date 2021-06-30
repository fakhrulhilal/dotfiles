function Clear-DevOpsWorkItemCommits {
    param(
		[int]
		[Parameter(Position=0, Mandatory=$True, ValueFromPipeline=$true)]
		$ID,

        [Parameter(Mandatory=$True)]
        [ValidateSet('Git', 'TFVC')]
        $Type,

        [string]
        $HistoryMessage,

        [switch]
        $DuplicateOnly,
        
        [switch]
        $Test,
		
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
        $GetResult = Invoke-RestMethod -Method Get -Headers $Header -Uri $Url

        $DuplicateMessages = @()
        $Payload = @()
        $Payload += @{
            op = 'test'
            path = '/rev'
            value = $GetResult.rev
        }
        $TypeMatching = switch($Type) {
            Git { 'vstfs:///Git/Commit*' }
            TFVC { 'vstfs:///VersionControl*' }
        }
<#
        $Commits = $GetResult.relations | %{ @{
            op = 'remove'
            path = "/relations/$($GetResult.relations.IndexOf($_))"
            comment = $_.attributes.comment
        } }
        $Payload += $Commits
#>

        $GetResult.relations | ?{ 'ArtifactLink' -eq $relation.rel -and $relation.url -ilike $TypeMatching } `
            | %{ $Payload += @{
                op = 'remove'
                path = "/relations/$($GetResult.relations.IndexOf($_))"
            } }

        $ValidateOnly = if ($Test) { '&validateOnly=True' } else { '' }
        $Header['Content-Type'] = 'application/json-patch+json'
        $Url = "$ApiUrl/wit/workitems/$ID/?`$expand=relations$ValidateOnly&api-version=5.1"
        if (![string]::IsNullOrWhiteSpace($HistoryMessage)) {
            $Payload += @{
                op = 'add'
                path = '/fields/System.History'
                value = $HistoryMessage
            }
        }
        $Body = ConvertTo-Json $Payload
        if ($Test) {
            Write-Host $Body
            Write-Host $Url
        }
        $Result = Invoke-RestMethod -Method Patch -Headers $Header -Uri $Url -Body $Body
        $Result
    }
}