/*
  LogAnalyzer rules (YARA-lite subset, see docs/dfir/DETECTION.md).
  Written from the technique, not from one sample. Validated: matches the MSBuild loader of the 2026-09-19 incident and
  none of the 500+ .targets/.props files of the .NET SDK (92 of which use $([System.…]) property functions legitimately).
*/
rule MSBuild_PropertyFunction_EntityObfuscation : msbuild obfuscation
{
    meta:
        description = "MSBuild project whose $([System.…]::…) property function hides the .NET type name with XML numeric character references"
        mitre = "T1127.001"
        severity = "high"
        version = "1"
    strings:
        $project = "<Project" nocase
        $pf_entity = /\$\(\[System\.[A-Za-z.]{0,30}&#[0-9]{2,3};/
    condition:
        $project and #pf_entity >= 1
}
