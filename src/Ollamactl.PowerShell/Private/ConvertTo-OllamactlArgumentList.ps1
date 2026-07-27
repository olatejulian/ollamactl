function ConvertTo-OllamactlArgumentList {
  <#
  .SYNOPSIS
  Converts structured parameters to an ollamactl argument list.

  .DESCRIPTION
  Builds the common --output json, --host, --timeout, and --config-dir options
  followed by the supplied command tokens. Each value remains a distinct
  process argument.

  .PARAMETER Command
  Specifies the command and subcommand tokens.

  .PARAMETER OllamaHost
  Overrides OLLAMA_HOST for the command.

  .PARAMETER TimeoutSeconds
  Specifies the HTTP timeout in seconds.

  .PARAMETER ConfigDirectory
  Specifies the ollamactl configuration directory.

  .EXAMPLE
  ConvertTo-OllamactlArgumentList -Command @('model', 'list') -TimeoutSeconds 30

  Creates arguments for a structured model-list call.

  .OUTPUTS
  System.String

  .NOTES
  This is an internal module function.
  #>
  [CmdletBinding()]
  [OutputType([string[]])]
  param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Command,

    [string]$OllamaHost,

    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds,

    [string]$ConfigDirectory
  )

  $Arguments = [System.Collections.Generic.List[string]]::new()
  $Arguments.Add('--output')
  $Arguments.Add('json')

  if (-not [string]::IsNullOrWhiteSpace($OllamaHost)) {
    $Arguments.Add('--host')
    $Arguments.Add($OllamaHost)
  }

  if ($PSBoundParameters.ContainsKey('TimeoutSeconds')) {
    $Arguments.Add('--timeout')
    $Arguments.Add($TimeoutSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }

  if (-not [string]::IsNullOrWhiteSpace($ConfigDirectory)) {
    $Arguments.Add('--config-dir')
    $Arguments.Add($ConfigDirectory)
  }

  foreach ($Token in $Command) {
    $Arguments.Add($Token)
  }

  return $Arguments.ToArray()
}
