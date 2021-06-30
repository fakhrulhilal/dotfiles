function Add-Number {
    [CmdletBinding()]
    param(
        [string]
        [Parameter(Mandatory=$true, ValueFromPipeline=$true)]
        $Path
    )

    Begin {
        $SeqMatch = [regex]::new('^(?<seq>\d+)\-(?<name>.+)\.sql$')
    }

    Process {
        $Item = Get-Item $Path
        $Match = $SeqMatch.Match($Item.Name)
        if (-not($Match.Success)) {
            Write-Verbose "'$($Item.Name)' doesn't match"
            return
        }

        $Seq = [int]::Parse($Match.Groups['seq'].Value)
        $NewName = [string]::Format('{0:000}-{1}.sql', $Seq, $Match.Groups['name'].Value)
        Write-Verbose "Renaming from '$Item' to '$NewName'"
        Rename-Item -Path $Item -NewName $NewName
    }
}