function ConvertTo-Refit {
    param(
        [string]
        [Parameter(Mandatory=$true, Position=0)]
        [ValidateNotNullOrEmpty()]
        $SwaggerUri,

        [string]
        [Parameter(Mandatory=$false)]
        [ValidateNotEmpty()]
        $Namespace,

        [string]
        [Parameter(Mandatory=$false)]
        [ValidateScript({ Test-Path -Path $_ -PathType Container })]
        $OutputPath = (Get-Location)
    )

    Process {
        $Swagger = Invoke-WebRequest -Uri $SwaggerUri | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($Namespace)) {
            $Namespace = Format-PascalCase $Swagger.info.title
        }

        foreach ($Class in $Swagger.components.schemas.psobject.Properties) {
            Write-Class -Path $OutputPath -Namespace $Namespace -Name $Class.Name -InputObject $Class.Value
        }
    }
}

function Format-PascalCase {
    param(
        [string]
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [ValidateNotNullOrEmpty()]
        $Text
    )

    Begin {
        $TextInfo = (Get-Culture).TextInfo
    }

    Process {
        $Text = $Text -replace '\W', ' ' -replace '_', ' '
        $Namespace = $TextInfo.ToTitleCase($Text)
        return ($Namespace -replace ' ')
    }
}

function Write-Class {
    param(
        [string]$Path,
        [string]$Namespace,
        [string]$Name,
        [pscustomobject]$InputObject
    )

    Process {
        $ClassName = Format-PascalCase $Name
        $ClassPath = [System.IO.Path]::Combine($Path, $ClassName) + '.cs'
        $ClassMeta = ConvertFrom-Swagger -InputObject $InputObject
        $Content = [string]@()
        Set-Content -Path $ClassPath -Value ''
        foreach ($Namespace in $ClassMeta.NamespaceUsages) {
            $Content += "using $Namespace;"
        }
        $Content += [System.Environment]::NewLine
        $Content += "namespace $Namespace"
        $Content += "{"
        $Content += "`tpublic class $ClassName"
        $Content += "`t{"
        foreach ($Property in $InputObject.Properties) {
            $Meta = [MetaProperty]$Property
            $BodyBuilder = [string]@()
            if (-not([string]::IsNullOrEmpty($Meta.Getter))) {
                $BodyBuilder += "get => $($Meta.Getter);"
            }
            if (-not([string]::IsNullOrEmpty($Meta.Setter))) {
                $BodyBuilder += "set => $($Meta.Setter);"
            }
            if ($BodyBuilder.Length -eq 0) {
                $Content += "`t`tpublic $($Meta.Type) $($Meta.Name) { get; set; }"
            } else {
                $Content += "`t`t{"
                $Content += [string]::Join("`t`t`t", $BodyBuilder)
                $Content += "`t`t}"
            }
        }
        $Content += "`t{"
        $Content += "}"
    }
}

function ConvertFrom-Swagger {
    param(
        [pscustomobject]
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        $InputObject
    )

    Process {
        [string[]]$NamespaceUsages = @()
        [MetaProperty[]]$Properties = @()
        foreach ($Property in $InputObject.properties.psobject.Properties) {
            $Meta = [MetaProperty]::new()
            $NamespaceUsages += 'System.Text.Json.Serialization'
            $Meta.Attributes += "[JsonPropertyName(`"$($Property.Name)`")]"
            $Meta.Name = Format-PascalCase $Property.Name
            switch ($Property.Value.type) {
                'integer' {
                    $Meta.Type = if ('int64' -eq $Property.Value.format) { 'long' } else { 'int' }
                }
                'number' {
                    $Meta.Type = if (-not([string]::IsNullOrEmpty([string]$Property.Value.format))) { $Property.Value.format } else { 'double' }
                }
                'boolean' {
                    $Meta.Type = 'bool'
                }
                'string' {
                    switch ($Property.Value.format) {
                        'date' {
                            $BackingProperty = [MetaProperty]::new()
                            $BackingProperty.Name = $Meta.Name
                            $BackingProperty.Type = 'DateTime'
                            $BackingProperty.Attributes += "[JsonIgnore]"
                            $BackingProperty.Getter = "DateTime.ParseExact($($Meta.Name)AsString, `"yyyy-MM-dd`", null)"
                            $BackingProperty.Setter = "$($Meta.Name)AsString = value.ToString(`"yyyy-MM-dd`")"
                            $Properties += $BackingProperty

                            $NamespaceUsages += 'System'
                            $Meta.Type = 'string'
                            $Meta.Name += 'AsString'
                        }
                        'date-time' {
                            $BackingProperty = [MetaProperty]::new()
                            $BackingProperty.Name = $Meta.Name
                            $BackingProperty.Type = 'DateTime'
                            $BackingProperty.Attributes += "[JsonIgnore]"
                            $BackingProperty.Getter = "DateTime.ParseExact($($Meta.Name)AsString, `"o`", null)"
                            $BackingProperty.Setter = "$($Meta.Name)AsString = value.ToString(`"o`")"
                            $Properties += $BackingProperty

                            $NamespaceUsages += 'System'
                            $Meta.Type = 'DateTime'
                            $Meta.Name += 'AsString'
                        }
                        'byte' {
                            $BackingProperty = [MetaProperty]::new()
                            $BackingProperty.Name = $Meta.Name
                            $BackingProperty.Type = 'byte[]'
                            $BackingProperty.Attributes += "[JsonIgnore]"
                            $BackingProperty.Getter = "Convert.FromBase64String($($Meta.Name)AsBase64)"
                            $Properties += $BackingProperty

                            $NamespaceUsages += 'System.Text'
                            $Meta.Type = 'string'
                            $Meta.Name += 'AsBase64'
                        }
                        'binary' {
                            $BackingProperty = [MetaProperty]::new()
                            $BackingProperty.Name = $Meta.Name
                            $BackingProperty.Type = 'byte[]'
                            $BackingProperty.Attributes += "[JsonIgnore]"
                            $BackingProperty.Getter = "Encoding.Default.GetString(Encoding.Default.GetBytes($($Meta.Name)AsString))"
                            $BackingProperty.Setter = "$($Meta.Name)AsString = BitConverter.ToString(value).Replace(`"-`", string.Empty)"
                            $Properties += $BackingProperty

                            $NamespaceUsages += 'System.Text'
                            $Meta.Type = 'string'
                            $Meta.Name += 'AsString'
                        }
                        Default {
                            $Meta.Type = 'string'
                        }
                    }
                }
                Default {
                    $Meta.Type = $Property.Value.type
                }
            }
            if ($InputObject.required -notcontains $Property.Name) {
                $Meta.Type += '?'
            }
            $Properties += $Meta
        }
        $NamespaceUsages = $NamespaceUsages | select -Unique | sort
        return [pscustomobject]@{
            NamespaceUsages = $NamespaceUsages
            Properties = $Properties
        }
    }
}

class MetaProperty {
    [string]$Type
    [string]$Name
    [string[]]$Attributes = @()
    [string]$Getter
    [string]$Setter
}