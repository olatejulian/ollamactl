function Import-DotEnv {

    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [switch]$ExportToProcess
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Environment file not found: $Path"
    }

    $Variables = [ordered]@{}

    foreach ($RawLine in Get-Content -LiteralPath $Path) {

        $Line = $RawLine.Trim()

        # Skip blanks and comments
        if ([string]::IsNullOrWhiteSpace($Line)) { continue }
        if ($Line.StartsWith("#")) { continue }

        # Strip optional inline comment after the value
        if ($Line -match '^([^#]+?)\s*#\s*(.*)$') {
            $Line = $Matches[1].Trim()
            if ([string]::IsNullOrWhiteSpace($Line)) { continue }
        }

        if ($Line -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
            throw "Invalid env line in $Path`: $RawLine"
        }

        $Key   = $Matches[1]
        $Value = $Matches[2].Trim()

        # Optional surrounding quotes
        if ($Value.Length -ge 2) {
            $First = $Value[0]
            $Last  = $Value[-1]
            if (($First -eq '"' -and $Last -eq '"') -or
                ($First -eq "'" -and $Last -eq "'")) {
                $Value = $Value.Substring(1, $Value.Length - 2)
            }
        }

        $Variables[$Key] = $Value

        if ($ExportToProcess) {
            [Environment]::SetEnvironmentVariable($Key, $Value, "Process")
        }
    }

    return $Variables
}