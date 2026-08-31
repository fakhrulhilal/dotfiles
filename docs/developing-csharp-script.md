# Developing C# Script

C# will use AoT by default. It's important to leverage all libraries that is compatible with AoT as much as possible. 
The C# script will primarily be used for scripting which is deliverable as code rather than binary. This repo uses:
1. [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework), for parsing CLI command
2. [SpectreConsole](https://spectreconsole.net), for building CLI interface (table, progress bar, live display, etc)
3. [PrettyConsole](https://github.com/dusrdev/PrettyConsole), a slimmer version of SpectreConsole for printing colored text
4. [System.Text.Json](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Json/docs/ThreatModel.md), 
   an AoT friendly JSON library which not only be used for web stuff, but also for querying DB data.

If some of the libraries are not AoT compabible, then we have to embed into binary itself. See sample in 
[`csharp/KafkaCliTrimmerRoots.xml`](../csharp/KafkaCliTrimmerRoots.xml), use AI to determine which library to embed. It is 
important to name the XML file as the same as the CLI script.

## Creating CLI Script

A minimum C# script should contain at least:
1. Shebang line (`#!/usr/bin/env dotnet --`), be sure to add '--' at the end, so our script arg will be parsed properly
2. A `ExperimentalFileBasedProgramEnableTransitiveDirectives` so we can include another .cs file (subject to change in the future)
3. A `AssemblyName` property to take out `Cli` suffix, prefer all lower case 
4. Be sure to add executable permission before committing to git repo: `chmod +x MyScript.cs`.

It's highly suggested to lock at least major versions when using the NuGet package.

<details>
    <summary>Sample</summary>

```shell
#!/usr/bin/env -S dotnet --

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property AssemblyName=myapp

#:include ./helpers/HttpHelper.cs
#:include ./models/Result.cs

#:package ConsoleAppFramework@5.7.13
#:package NetEscapades.EnumGenerators@1.0.0-beta21*
#:package Spectre.Console@0.57.2
```
</details>

## Compiling and Publishing

Read the full guide from [official documentation](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps). 
<details>
    <summary>dotnet build</summary>

```shell
dotnet clean Script.cs
dotnet build Script.cs
dotnet publish -c Release MyScriptCli.cs

# rename binary
dotnet publish -c Release /p:AssemblyName=myapp MyScriptCli.cs
```
</details>

Take a look at [official sample](https://github.com/dotnet/dotnet-docker/tree/main/samples/releasesapi) for building 
docker container image. Basically, we are targeting dependency only image rather than full runtime. It's also be safer 
to use extra image which contains time zone and ICU data. 

<details>
    <summary>Docker build</summary>

```shell
# use extra image which contains time zone and ICU data
docker build --build-arg SCRIPT_FILE=MyScriptCli.cs .

# use another ubuntu distro
docker build --build-arg SCRIPT_FILE=MyScriptCli.cs --build-arg DISTRO=resolute --tag myapp:resolute .

# using alpine image
docker build --build-arg SCRIPT_FILE=MyScriptCli.cs --tag myapp:alpine -f alpine.Dockerfile .

# build for all os arch
docker buildx create --name multiarch --driver docker-container --use
docker buildx inspect --bootstrap
BUILD_TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ) GIT_COMMIT_SHA="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" docker compose build --no-cache --builder multiarch --push
```
</details>