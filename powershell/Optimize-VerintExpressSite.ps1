function Optimize-VerintExpressSite {
	param(
		[Parameter(
			Mandatory=$True,
			HelpMessage="Path to Verint Express deployment directory")]
		[ValidateScript({ Test-Path $_ -PathType Container })]
		[string]
		[string]$Path
	)

	$OldPath = (Get-Item -Path .).FullName
	$Path = (Get-Item -Path $Path).FullName
	Set-Location $Path

	$WebsiteName = (Get-Item -Path $Path).Name
	$WindowsServices = @("VerintExpress_$($WebsiteName)_ImportEmail", "VerintExpress_$($WebsiteName)_Emailing", "VerintExpress_$($WebsiteName)_TaskEmailReminder")
	ForEach ($Service in $WindowsServices) {
		Stop-Service -Name $Service -Force | Out-Null
	}
	$ShareFolder = [System.IO.Path]::Combine($Path, 'SharedFiles')
	If (-not(Test-Path $ShareFolder -PathType Container)) {
		New-Item -Path $ShareFolder -ItemType Directory -Force | Out-Null
	}

	class LibraryMetadata {
		[string[]]$Paths
		[string]$FileName
		[string]$Hash
		[long]$Size
		[string]$ClonePath

		LibraryMetadata(
			[string]$hash,
			[System.IO.FileInfo]$fileInfo
		) {
			$this.FileName = $fileInfo.Name
			$this.Hash = $hash
			$this.Size = $fileInfo.Length
			$this.Paths = @()
		}
	}

	$BigFolders = Get-ChildItem -Path $Path -Recurse -Include XULRunner* -Directory
	ForEach ($Folder in $BigFolders) {
		$ClonePath = [System.IO.Path]::Combine($ShareFolder, $Folder.Name)
		$RelativePath = Resolve-Path -Path $Folder.FullName -Relative
		If (-not(Test-Path -Path $ClonePath -PathType Container)) {
			Move-Item -Path $Folder.FullName -Destination $ClonePath -Force | Out-Null
		} Else {
			Remove-Item -Path $Folder.FullName -Recurse -Force -ErrorAction Stop | Out-Null
		}
		$ClonePath = Resolve-Path -Path $ClonePath -Relative
		Write-Host "Linking $RelativePath to $ClonePath"
		New-Item -Path $RelativePath -ItemType Junction -Value $ClonePath -Force -ErrorAction Stop | Out-Null
	}

	$Statistic = @{}
	$DuplicateFiles = Get-ChildItem -Path $Path -Recurse -Include *.dll,*.kdf,*.lex,*.exe,*.tdf -File | ?{ $_.Directory.FullName -notlike "$ShareFolder\*" }
	ForEach ($File in $DuplicateFiles) {
		$Hash = (Get-FileHash -Path $File.FullName -Algorithm MD5).Hash
		$ClonePath = [System.IO.Path]::Combine($ShareFolder, "$($File.BaseName)#$($Hash)$($File.Extension)")
		If (-not(Test-Path -Path $ClonePath -PathType Leaf)) {
			Copy-Item -Path $File.FullName -Destination $ClonePath -ErrorAction Stop | Out-Null
			$Statistic[$Hash] = [LibraryMetadata]::new($Hash, $File)
			$Statistic[$Hash].ClonePath = Resolve-Path -Path $ClonePath -Relative
		}
		$Statistic[$Hash].Paths += $File.FullName
		$RelativePath = Resolve-Path -Path $File.FullName -Relative
		Remove-Item -Path $File.FullName -Force -ErrorAction Stop | Out-Null
		Write-Host "Linking $RelativePath to $($Statistic[$Hash].ClonePath)"
		New-Item -Path $RelativePath -ItemType SymbolicLink -Value $Statistic[$Hash].ClonePath -Force -ErrorAction Stop | Out-Null
	}

	ForEach ($Service in $WindowsServices) {
		Start-Service -Name $Service | Out-Null
	}
	Set-Location $OldPath
}