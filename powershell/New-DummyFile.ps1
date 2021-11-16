function New-DummyFile {
	param(
		[string]$Identifier,
		[string]$Path = (Get-Location),
		[int]$Minimum = 2,
		[int]$Maximum = 5
	)

	Process {
		$Total = Get-Random -Minimum $Minimum -Maximum $Maximum
		for ($i = 1; $i -le $Total; $i++) {
			$FileName = "$Identifier-$i.txt"
			Set-Content -Path (Join-Path -Path $Path -ChildPath $FileName) -Value "$FileName content"
		}
	}
}