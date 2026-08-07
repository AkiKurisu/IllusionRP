[CmdletBinding()]
param(
    [switch] $All
)

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot))
{
    Write-Host "line-ending check: unable to locate the Git repository." -ForegroundColor Red
    exit 1
}

Push-Location $repoRoot
try
{
    if ($All)
    {
        $paths = @(& git -c core.quotepath=false ls-files --cached)
    }
    else
    {
        # Inspect the working-tree version of files participating in this commit.
        # This catches mixed endings produced by editors or patch tools even when
        # Git's clean conversion would normalize the staged blob.
        $paths = @(& git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR --no-renames)
    }

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "line-ending check: unable to enumerate files." -ForegroundColor Red
        exit 1
    }

    $invalidFiles = [System.Collections.Generic.List[object]]::new()

    foreach ($relativePath in $paths)
    {
        if ([string]::IsNullOrWhiteSpace($relativePath) -or -not (Test-Path -LiteralPath $relativePath -PathType Leaf))
        {
            continue
        }

        $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $relativePath))
        if ($bytes.Length -eq 0 -or [Array]::IndexOf($bytes, [byte] 0) -ge 0)
        {
            continue
        }

        $crlfCount = 0
        $lfCount = 0
        $crCount = 0

        for ($index = 0; $index -lt $bytes.Length; $index++)
        {
            if ($bytes[$index] -eq 13)
            {
                if (($index + 1) -lt $bytes.Length -and $bytes[$index + 1] -eq 10)
                {
                    $crlfCount++
                    $index++
                }
                else
                {
                    $crCount++
                }
            }
            elseif ($bytes[$index] -eq 10)
            {
                $lfCount++
            }
        }

        if (($crlfCount -gt 0 -and $lfCount -gt 0) -or $crCount -gt 0)
        {
            $invalidFiles.Add([PSCustomObject]@{
                Path = $relativePath
                CRLF = $crlfCount
                LF = $lfCount
                CR = $crCount
            })
        }
    }

    if ($invalidFiles.Count -gt 0)
    {
        Write-Host "Commit blocked: inconsistent line endings were found:" -ForegroundColor Red
        foreach ($file in $invalidFiles)
        {
            Write-Host ("  {0} (CRLF={1}, LF={2}, CR={3})" -f $file.Path, $file.CRLF, $file.LF, $file.CR)
        }

        Write-Host "Normalize each file to a single line-ending style, stage it again, and retry." -ForegroundColor Yellow
        Write-Host "For this repository's Windows convention, use .githooks/normalize-line-endings.ps1." -ForegroundColor Yellow
        exit 1
    }

    exit 0
}
finally
{
    Pop-Location
}
