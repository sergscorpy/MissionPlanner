$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio C# compiler was not found.' }
$buildDir = Join-Path $repo 'bin\Debug\net461\plugins'
$assembly = Join-Path $buildDir 'RCListener.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build RCListener in Debug first.' }
$testExe = Join-Path $buildDir 'RCListener.PitchTests.exe'
& $compiler /nologo /target:exe "/out:$testExe" "/reference:$assembly" (Join-Path $PSScriptRoot 'TopotekPitchTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Pitch tests failed to compile.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Pitch tests failed.' }
