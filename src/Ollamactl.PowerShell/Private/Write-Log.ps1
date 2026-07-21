function Write-Log {

  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [ValidateSet(
      "Debug",
      "Information",
      "Warning",
      "Error"
    )]
    [string]$Level,

    [Parameter(Mandatory)]
    [string]$Message,

    [string]$HomePath
  )

  $Timestamp =
  Get-Date -Format "yyyy-MM-dd HH:mm:ss"

  $Entry =
  "[$Timestamp] [$Level] $Message"

  switch ($Level) {

    "Debug" {

      Write-Debug $Message
    }

    "Information" {

      Write-Information `
        $Message `
        -InformationAction Continue
    }

    "Warning" {

      Write-Warning $Message
    }

    "Error" {

      Write-Error $Message
    }
  }

  $Paths = Get-OllamaPaths -HomePath $HomePath

  New-Item `
    -ItemType Directory `
    -Path $Paths.LogDirectory `
    -Force |
    Out-Null

  Add-Content `
    -Path (
    Join-Path `
      $Paths.LogDirectory `
      "application.log"
  ) `
    -Value $Entry
}
