[CmdletBinding()]
param()

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot))
{
    Write-Error "Unable to locate the Git repository."
    exit 1
}

git -C $repoRoot config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0)
{
    Write-Error "Unable to configure core.hooksPath."
    exit 1
}

Write-Host "Git hooks enabled for $repoRoot"
