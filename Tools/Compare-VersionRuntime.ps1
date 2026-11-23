param([Parameter(Mandatory=$true)][string]$Before,[Parameter(Mandatory=$true)][string]$After,[Parameter(Mandatory=$true)][string]$Report)
$ErrorActionPreference='Stop'
Add-Type -Path (Resolve-Path 'Artifacts/PumpingProject-20261121/Library/PackageCache/com.unity.nuget.mono-cecil@1.11.4/Mono.Cecil.dll').Path
function VisitTypes($types){foreach($type in $types){$type;VisitTypes $type.NestedTypes}}
function Fingerprints($path){
    $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $path).Path)
    try {
        $result=@{}
        foreach($type in (VisitTypes $assembly.MainModule.Types)){
            foreach($method in $type.Methods){
                if(-not $method.HasBody){continue}
                $instructions=@($method.Body.Instructions|Where-Object {$_.OpCode.Name -ne 'nop'})
                $lines=foreach($instruction in $instructions){
                    $operand=$instruction.Operand
                    if($method.FullName -eq 'System.Void Ninjas4744.MoSim.Turbo4744NativeReplay/Report::.ctor()' -and $instruction.OpCode.Name -eq 'ldstr' -and $operand -in @('1.0.1','1.0.2')){$operand='<release-version>'}
                    if($operand -is [Mono.Cecil.Cil.Instruction]){
                        while($operand.OpCode.Name -eq 'nop'){$operand=$operand.Next}
                        $operand='branch:'+([Array]::IndexOf($instructions,$operand))
                    } elseif($operand -is [Mono.Cecil.Cil.Instruction[]]){
                        $operand=@($operand|ForEach-Object {$jump=$_;while($jump.OpCode.Name -eq 'nop'){$jump=$jump.Next};[Array]::IndexOf($instructions,$jump)}) -join ','
                    } elseif($null -ne $operand){$operand=$operand.ToString()}
                    [regex]::Replace([regex]::Replace(($instruction.OpCode.Name+' '+$operand),'__JobReflectionRegistrationOutput__\d+','__JobReflectionRegistrationOutput__<generated>'),'Turbo4744/<>c__DisplayClass(?:178|179)_0','Turbo4744/<>c__DisplayClass<LateInit>')
                }
                $key=[regex]::Replace([regex]::Replace($method.FullName,'__JobReflectionRegistrationOutput__\d+','__JobReflectionRegistrationOutput__<generated>'),'Turbo4744/<>c__DisplayClass(?:178|179)_0','Turbo4744/<>c__DisplayClass<LateInit>')
                $result[$key]=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).ToLowerInvariant()
            }
        }
        return $result
    } finally {$assembly.Dispose()}
}
$old=Fingerprints $Before
$new=Fingerprints $After
$changed=@($new.Keys|Where-Object {$old[$_] -ne $new[$_]}|Sort-Object)
$removed=@($old.Keys|Where-Object {-not $new.ContainsKey($_)})
$passed=$changed.Count -eq 0 -and $removed.Count -eq 0 -and $new.Count -eq $old.Count
[ordered]@{passed=$passed;before_dll_sha256=(Get-FileHash -LiteralPath $Before).Hash.ToLower();after_dll_sha256=(Get-FileHash -LiteralPath $After).Hash.ToLower();compared_methods=$new.Count;changed_behavior_methods=$changed;removed_methods=$removed;normalization='Debug nops, branch labels, generated job-registration identifiers, the LateInit closure ordinal 178/179, and only the native replay Report constructor release-version literal (1.0.1 to 1.0.2). All other operations, constants and call targets remain exact.';before_methods=$old;after_methods=$new}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Report
Write-Output ('Version-only runtime equivalence: '+$passed+'; methods='+$new.Count)
if(-not $passed){throw 'Runtime behavior differs from the owner-tested trial'}
