function ConvertTo-Mockaco {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [object]
        $Har,

        # Output path to Mockaco Mocks folder
        [Parameter(Mandatory=$false)]
        [string]
        $OutputPath = (Get-Location),

        # Folder name containing static files
        [Parameter(Mandatory=$false)]
        [string]
        $StaticFolder = 'files'
    )

    Begin {
        $Mapping = @{
            gif = 'image/gif'
            jpg = 'image/jpeg'
            jpeg = 'image/jpeg'
            png = 'image/png'
            ico = 'image/x-icon'
            js = 'text/javascript; charset=utf-8'
            css = 'text/css; charset=utf-8'
        }
    }
    Process {
        if (-not(Test-Path -Path "$OutputPath\$StaticFolder")) {
            New-Item -ItemType Directory -Path "$OutputPath\$StaticFolder" | Out-Null
        }
        foreach ($Entry in $Har.log.entries) {
            $Request = ConvertTo-Request -Entry $Entry
            $SuggestedFilename = $Request.SuggestedFilename
            $Request.Remove('SuggestedFilename')
            $Response = ConvertTo-Response -Entry $Entry -OutputPath $OutputPath -StaticFolder $StaticFolder -MimeMapping $Mapping
            $Metadata = @{
                request = $Request
                response = $Response
            }
            $Json = ConvertTo-Json $Metadata
            Set-Content -Path (Join-Path -Path $OutputPath -ChildPath $SuggestedFilename) -Value $Json
        }
    }
}

function ConvertTo-Request {
    param (
        [object]
        $Entry
    )

    Process {
        $Request = $Entry.request
        $Uri = [System.Uri]::new($Request.url)
        $Filename = [System.IO.Path]::GetFileName($Uri.AbsolutePath)
        $Output = @{ 
            method = $Request.method
            route = $Uri.AbsolutePath.TrimStart('/')
            SuggestedFilename = "$($Filename).json"
        }

        return $Output
    }
}

function ConvertTo-Response {
    param (
        [object]
        $Entry,

        [string]
        $OutputPath,

        [string]
        $StaticFolder,

        [hashtable]
        $MimeMapping
    )

    Begin {
        $IgnoredHeaders = 'Date', 'Server', 'Accept-Ranges', 'Last-Modified', 'Content-Encoding', 'Transfer-Encoding', 'Content-Length', 'Keep-Alive', 'Connection'
    }

    Process {
        $Uri = [System.Uri]::new($Entry.request.url)
        $Filename = [System.IO.Path]::GetFileName($Uri.AbsolutePath)
        if ($Entry.response.content.mimeType -like 'text/html*') {
            $Filename += ".html"
        }
        $Output = @{
            status = 'OK'
            headers = @{}
            file = "Mocks/$StaticFolder/$Filename"
        }
        $ExposedHeaders = $Entry.response.headers | ?{ $IgnoredHeaders -notcontains $_.name }
        foreach ($Header in $ExposedHeaders) {
            $Output.headers[$Header.name] = $Header.value
        }
        $AllHeaders = $Output.headers.Keys | %{ $_.ToLower() }
        if ($AllHeaders -notcontains 'content-type') {
            $FileExtension = [System.IO.Path]::GetExtension($Filename).Replace('.', '')
            if ($MimeMapping.ContainsKey($FileExtension)) {
                $Output.headers.Add('Content-Type', $MimeMapping[$FileExtension])
            }
        }
        $FilePath = Join-Path -Path $OutputPath -ChildPath "$StaticFolder\$Filename"
        if ($Entry.response.content.encoding -eq 'base64') {
            $Bytes = [System.Convert]::FromBase64String($Entry.response.content.text)
            [System.IO.File]::WriteAllBytes($FilePath, $Bytes)
        }
        else {
            Set-Content -Path $FilePath -Value $Entry.response.content.text
        }
        return $Output
    }
}