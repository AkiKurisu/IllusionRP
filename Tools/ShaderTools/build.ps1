$ErrorActionPreference = 'Stop'
dotnet run --project "$PSScriptRoot/Tests/ShaderTools.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'ShaderTools tests failed' }
$destination = Join-Path $PSScriptRoot '../../Packages/com.kurisu.illusion-render-pipelines/Editor/Plugins/ShaderTools'
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/netstandard2.0/Illusion.ShaderTools.dll" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot/ThirdParty/UnityShaderParser/LICENSE" -Destination "$destination/ThirdPartyNotices.txt"
