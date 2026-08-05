[CmdletBinding()]
param(
    [ValidateSet("CRLF", "LF")]
    [string] $Style = "CRLF",

    [Parameter(Mandatory, Position = 0)]
    [string[]] $Path
)

$lineEnding = if ($Style -eq "CRLF") { [byte[]] (13, 10) } else { [byte[]] (10) }

foreach ($item in $Path)
{
    $resolvedPath = Resolve-Path -LiteralPath $item -ErrorAction Stop
    $bytes = [IO.File]::ReadAllBytes($resolvedPath)

    if ([Array]::IndexOf($bytes, [byte] 0) -ge 0)
    {
        throw "Refusing to normalize binary file: $item"
    }

    $output = [IO.MemoryStream]::new($bytes.Length + 16)
    try
    {
        for ($index = 0; $index -lt $bytes.Length; $index++)
        {
            if ($bytes[$index] -eq 13)
            {
                if (($index + 1) -lt $bytes.Length -and $bytes[$index + 1] -eq 10)
                {
                    $index++
                }

                $output.Write($lineEnding, 0, $lineEnding.Length)
            }
            elseif ($bytes[$index] -eq 10)
            {
                $output.Write($lineEnding, 0, $lineEnding.Length)
            }
            else
            {
                $output.WriteByte($bytes[$index])
            }
        }

        [IO.File]::WriteAllBytes($resolvedPath, $output.ToArray())
    }
    finally
    {
        $output.Dispose()
    }

    Write-Host "Normalized $item to $Style."
}
