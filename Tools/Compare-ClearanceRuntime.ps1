param(
    [Parameter(Mandatory=$true)][string]$Before,
    [Parameter(Mandatory=$true)][string]$After,
    [Parameter(Mandatory=$true)][string]$Report
)
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
                    if($method.FullName -eq 'System.Void Ninjas4744.MoSim.Turbo4744NativeReplay/Report::.ctor()' -and $instruction.OpCode.Name -eq 'ldstr' -and $operand -in @('1.0.2','1.0.3')){$operand='<release-version>'}
                    if($operand -is [Mono.Cecil.Cil.Instruction]){
                        while($operand.OpCode.Name -eq 'nop'){$operand=$operand.Next}
                        $operand='branch:'+([Array]::IndexOf($instructions,$operand))
                    } elseif($operand -is [Mono.Cecil.Cil.Instruction[]]){
                        $operand=@($operand|ForEach-Object {$jump=$_;while($jump.OpCode.Name -eq 'nop'){$jump=$jump.Next};[Array]::IndexOf($instructions,$jump)}) -join ','
                    } elseif($null -ne $operand){$operand=$operand.ToString()}
                    [regex]::Replace(($instruction.OpCode.Name+' '+$operand),'__JobReflectionRegistrationOutput__\d+','__JobReflectionRegistrationOutput__<generated>')
                }
                $key=[regex]::Replace($method.FullName,'__JobReflectionRegistrationOutput__\d+','__JobReflectionRegistrationOutput__<generated>')
                $result[$key]=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).ToLowerInvariant()
            }
        }
        return $result
    } finally {$assembly.Dispose()}
}

# Follow the actual packaged IL before any file/native-loader calls. This
# checks the player assembly rather than inferring portability from source.
function EntersNativeLoader($method,[int]$platform,[bool]$editor,[bool]$loaded){
    $stack=[Collections.Generic.Stack[int]]::new()
    $locals=@{}
    $pc=$method.Body.Instructions[0]
    for($step=0;$step -lt 100;$step++){
        $op=$pc.OpCode.Name
        $next=$pc.Next
        switch -Regex ($op){
            '^nop$' {}
            '^call$' {
                switch($pc.Operand.Name){
                    'get_isEditor' {$stack.Push([int]$editor)}
                    'get_Loaded' {$stack.Push([int]$loaded)}
                    'get_platform' {$stack.Push($platform)}
                    default {return $true}
                }
            }
            '^ldc.i4.([0-8])$' {$stack.Push([int]$Matches[1])}
            '^ldc.i4(?:.s)?$' {$stack.Push([int]$pc.Operand)}
            '^ceq$' {$b=$stack.Pop();$a=$stack.Pop();$stack.Push([int]($a -eq $b))}
            '^or$' {$b=$stack.Pop();$a=$stack.Pop();$stack.Push($a -bor $b)}
            '^stloc.([0-9])$' {$locals[[int]$Matches[1]]=$stack.Pop()}
            '^ldloc.([0-9])$' {$stack.Push($locals[[int]$Matches[1]])}
            '^stloc(?:.s)?$' {$locals[$pc.Operand.Index]=$stack.Pop()}
            '^ldloc(?:.s)?$' {$stack.Push($locals[$pc.Operand.Index])}
            '^brtrue(?:.s)?$' {if($stack.Pop() -ne 0){$next=$pc.Operand}}
            '^brfalse(?:.s)?$' {if($stack.Pop() -eq 0){$next=$pc.Operand}}
            '^br(?:.s)?$' {$next=$pc.Operand}
            '^bne.un(?:.s)?$' {$b=$stack.Pop();$a=$stack.Pop();if($a -ne $b){$next=$pc.Operand}}
            '^beq(?:.s)?$' {$b=$stack.Pop();$a=$stack.Pop();if($a -eq $b){$next=$pc.Operand}}
            '^ret$' {return $false}
            '^ldarg.0$' {return $true}
            default {throw ('Unsupported guard instruction: '+$op)}
        }
        if(-not $next){throw 'Guard did not terminate'}
        $pc=$next
    }
    throw 'Guard did not terminate within 100 instructions'
}

function LoaderBody($method){
    $all=@($method.Body.Instructions|Where-Object {$_.OpCode.Name -ne 'nop'})
    $first=@($all|Where-Object {$_.OpCode.Name -eq 'ldarg.0'})[0]
    $instructions=$all[([Array]::IndexOf($all,$first))..($all.Length-1)]
    $lines=foreach($instruction in $instructions){
        $operand=$instruction.Operand
        if($operand -is [Mono.Cecil.Cil.Instruction]){
            while($operand.OpCode.Name -eq 'nop'){$operand=$operand.Next}
            $operand='branch:'+([Array]::IndexOf($instructions,$operand))
        } elseif($null -ne $operand){$operand=$operand.ToString()}
        $instruction.OpCode.Name+' '+$operand
    }
    return ($lines -join "`n")
}

$old=Fingerprints $Before
$new=Fingerprints $After
$changed=@($new.Keys|Where-Object {$old[$_] -ne $new[$_]}|Sort-Object)
$removed=@($old.Keys|Where-Object {-not $new.ContainsKey($_)})
$allowed='System.Void Ninjas4744.MoSim.Turbo4744NetBurst::Load(UnityEngine.TextAsset,System.String)'
if($changed.Count -ne 1 -or $changed[0] -ne $allowed -or $removed.Count -ne 0 -or $old.Count -ne $new.Count){throw ('Unexpected runtime changes: '+($changed -join ', '))}
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $After).Path)
$previous=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Before).Path)
try {
    $method=$assembly.MainModule.GetType('Ninjas4744.MoSim.Turbo4744NetBurst').Methods|Where-Object Name -eq 'Load'
    $oldMethod=$previous.MainModule.GetType('Ninjas4744.MoSim.Turbo4744NetBurst').Methods|Where-Object Name -eq 'Load'
    if((LoaderBody $method) -cne (LoaderBody $oldMethod)){throw 'Native loader body changed beyond the platform guard'}
    $guard=[ordered]@{
        windows_player_enters_loader=(EntersNativeLoader $method 2 $false $false)
        linux_player_skips_loader=(-not (EntersNativeLoader $method 13 $false $false))
        macos_player_skips_loader=(-not (EntersNativeLoader $method 0 $false $false))
        editor_skips_loader=(-not (EntersNativeLoader $method 2 $true $false))
        already_loaded_skips_loader=(-not (EntersNativeLoader $method 2 $false $true))
    }
    if($guard.Values -contains $false){throw 'Packaged platform guard failed'}
    $references=@($assembly.MainModule.AssemblyReferences|ForEach-Object Name)
    if($references|Where-Object {$_ -like 'UnityEditor*'}){throw 'Player assembly references UnityEditor'}
} finally {$assembly.Dispose();$previous.Dispose()}
[ordered]@{
    passed=$true
    before_dll_sha256=(Get-FileHash -LiteralPath $Before).Hash.ToLower()
    after_dll_sha256=(Get-FileHash -LiteralPath $After).Hash.ToLower()
    compared_methods=$new.Count
    changed_methods=$changed
    unchanged_recovery_and_drive_methods=$true
    windows_native_loader_body_unchanged=$true
    removed_methods=$removed
    platform_guard=$guard
    assembly_references=$references
    normalization='Debug nops, branch labels, generated job-registration identifiers, and only the native replay Report constructor version literal (1.0.2 to 1.0.3). The sole changed method is the explicitly checked net-loader platform guard.'
    before_methods=$old
    after_methods=$new
}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Report -Encoding utf8
Write-Output ('Trial runtime equivalence passed; methods='+$new.Count+'; portable loader gate passed')
