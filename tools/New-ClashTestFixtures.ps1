[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputFolder)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
# Two tiny, synthetic two-cube sources. No project/company geometry is used.
foreach($definition in @(
    @{Name='GEN-STR-Z01-001-R01.obj';Offset=@(0.0,0.0,0.0)},
    @{Name='GEN-MEP-Z01-002-R01.obj';Offset=@(0.4,0.3,0.2)}
)){
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add('# Disposable clash fixture; coordinates in arbitrary consistent units')
    for($cube=0;$cube -lt 2;$cube++){
        $lines.Add("o Cube_$cube")
        foreach($point in @(@(0,0,0),@(1,0,0),@(1,1,0),@(0,1,0),@(0,0,1),@(1,0,1),@(1,1,1),@(0,1,1))){
            $x=$point[0]+$definition.Offset[0]+$cube*10
            $y=$point[1]+$definition.Offset[1]
            $z=$point[2]+$definition.Offset[2]
            $lines.Add([string]::Format([Globalization.CultureInfo]::InvariantCulture,'v {0} {1} {2}',$x,$y,$z))
        }
        foreach($face in @(@(1,3,2),@(1,4,3),@(5,6,7),@(5,7,8),@(1,2,6),@(1,6,5),@(2,3,7),@(2,7,6),@(3,4,8),@(3,8,7),@(4,1,5),@(4,5,8))){
            $indices=@($face | ForEach-Object {$_+$cube*8})
            $lines.Add('f '+($indices -join ' '))
        }
    }
    [IO.File]::WriteAllLines((Join-Path $OutputFolder $definition.Name),$lines,[Text.UTF8Encoding]::new($false))
    $ifc=[Collections.Generic.List[string]]::new()
    $ifc.Add("ISO-10303-21;`nHEADER;`nFILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');`nFILE_NAME('Disposable clash fixture','2026-10-08T00:00:00',(''),(''),'BIM Tools fixture','BIM','');`nFILE_SCHEMA(('IFC4'));`nENDSEC;`nDATA;")
    $ifc.Add("#1=IFCCARTESIANPOINT((0.,0.,0.));`n#2=IFCAXIS2PLACEMENT3D(#1,$,$);`n#3=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-6,#2,$);`n#4=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);`n#5=IFCUNITASSIGNMENT((#4));`n#6=IFCPROJECT('0AAAAAAAAAAAAAAAAAAAAA',$,'Fixture',$,$,$,$,(#3),#5);`n#7=IFCLOCALPLACEMENT($,#2);`n#8=IFCBUILDINGSTOREY('0BBBBBBBBBBBBBBBBBBBBB',$,'Fixture storey',$,$,#7,$,$,.ELEMENT.,$);`n#9=IFCRELAGGREGATES('0CCCCCCCCCCCCCCCCCCCCC',$,$,$,#6,(#8));")
    $products=[Collections.Generic.List[string]]::new()
    for($cube=0;$cube -lt 2;$cube++){
        $points=@()
        foreach($point in @(@(0,0,0),@(1,0,0),@(1,1,0),@(0,1,0),@(0,0,1),@(1,0,1),@(1,1,1),@(0,1,1))){
            $x=$point[0]+$definition.Offset[0]+$cube*10; $y=$point[1]+$definition.Offset[1]; $z=$point[2]+$definition.Offset[2]
            $points+=[string]::Format([Globalization.CultureInfo]::InvariantCulture,'({0:F4},{1:F4},{2:F4})',$x,$y,$z)
        }
        $start=10+$cube*5; $coords=$start; $mesh=$start+1; $shape=$start+2; $definitionId=$start+3; $product=$start+4
        $faces='(1,3,2),(1,4,3),(5,6,7),(5,7,8),(1,2,6),(1,6,5),(2,3,7),(2,7,6),(3,4,8),(3,8,7),(4,1,5),(4,5,8)'
        $ifc.Add("#$coords=IFCCARTESIANPOINTLIST3D(("+($points -join ',')+'));')
        $ifc.Add("#$mesh=IFCTRIANGULATEDFACESET(#$coords,$,.T.,($faces),$);")
        $ifc.Add("#$shape=IFCSHAPEREPRESENTATION(#3,'Body','Tessellation',(#$mesh));")
        $ifc.Add("#$definitionId=IFCPRODUCTDEFINITIONSHAPE($,$,(#$shape));")
        $globalId=$(if($definition.Name -like '*STR*'){'0DDDDDDDDDDDDDDDDDDDD'}else{'0FFFFFFFFFFFFFFFFFFFF'})+$cube
        $ifc.Add("#$product=IFCBUILDINGELEMENTPROXY('$globalId',$,'Cube_$cube',$,$,#7,#$definitionId,$,.NOTDEFINED.);")
        $products.Add('#'+$product)
    }
    $ifc.Add("#20=IFCRELCONTAINEDINSPATIALSTRUCTURE('0EEEEEEEEEEEEEEEEEEEEE',$,$,$,("+($products -join ',')+"),#8);`nENDSEC;`nEND-ISO-10303-21;")
    [IO.File]::WriteAllLines((Join-Path $OutputFolder ([IO.Path]::ChangeExtension($definition.Name,'ifc'))),$ifc,[Text.UTF8Encoding]::new($false))
}
