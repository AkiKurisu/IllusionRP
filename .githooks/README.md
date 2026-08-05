# Repository Git hooks

Enable the versioned hooks once after cloning:

```powershell
& .\.githooks\install.ps1
```

The pre-commit hook checks files included in the commit and rejects files that
mix CRLF, LF, or standalone CR line endings. It checks the working-tree bytes
because Git may normalize the staged blob before the hook runs.

To scan every tracked file manually:

```powershell
& .\.githooks\check-line-endings.ps1 -All
```

To normalize selected files to the repository's Windows convention:

```powershell
& .\.githooks\normalize-line-endings.ps1 -Style CRLF -Path @(
    "Shaders/Example.shader",
    "Shaders/Example.hlsl"
)
```

The pre-commit hook never rewrites or stages files automatically, so partial
staging remains safe.
