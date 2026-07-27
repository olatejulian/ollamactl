@{
  Severity = @('Error', 'Warning')
  IncludeDefaultRules = $true
  Rules = @{
    PSProvideCommentHelp = @{
      Enable = $true
      ExportedOnly = $false
      BlockComment = $true
      Placement = 'begin'
    }
    PSUseConsistentIndentation = @{
      Enable = $true
      Kind = 'space'
      IndentationSize = 2
    }
    PSUseConsistentWhitespace = @{
      Enable = $true
      CheckInnerBrace = $true
      CheckOpenBrace = $true
      CheckOpenParen = $true
      CheckOperator = $true
      CheckPipe = $true
      CheckSeparator = $true
    }
    PSUseCorrectCasing = @{
      Enable = $true
    }
  }
}
