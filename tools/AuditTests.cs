using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM.Tools.Health;

internal static class AuditTests
{
    private static int checks;
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);checks++;}
    private static AuditFile FileEntry(string name,string source,string parent=null){return new AuditFile{Name=name,Source=source,ParentId=parent,NativeId=Guid.NewGuid().ToString("D")};}
    private static AuditSnapshot Two()
    {
        AuditFile a=FileEntry("GEN-STRUCTURE-ASM-R01.nwd","C:/Shared/Structure.nwd"),b=FileEntry("GEN-MEP-ASM.nwd","C:/Shared/MEP.nwd");
        return new AuditSnapshot{DocumentPath="C:/Project/LIVE.nwf",Files=new List<AuditFile>{a,FileEntry("COMMON.rvt","C:/Structure/COMMON.rvt",a.Id),b,FileEntry("COMMON.rvt","C:/MEP/COMMON.rvt",b.Id)}};
    }
    private static AuditSnapshot Reidentify(AuditSnapshot old)
    {
        AuditSnapshot copy=old.Copy();Dictionary<string,string> map=copy.Files.ToDictionary(f=>f.Id,f=>Guid.NewGuid().ToString("D"));
        foreach(AuditFile file in copy.Files){file.ParentId=string.IsNullOrEmpty(file.ParentId)?null:map[file.ParentId];file.Id=map[file.Id];file.NativeId=Guid.NewGuid().ToString("D");}return copy;
    }
    private static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
    public static int Main(string[] args)
    {
        try
        {
            string folder=args[0];Directory.CreateDirectory(folder);AuditSnapshot old=Two(),current=Reidentify(old);
            Check(AuditComparison.Compare(old,current).All(r=>r.Status==AuditStatus.Loaded),"Native GUID changes do not lose unchanged file identities");
            Check(AuditComparison.Compare(old,current).Count(r=>!r.File.Assembly&&r.Status==AuditStatus.Loaded)==2,"Same child name stays in its own assembly");
            current.Files.RemoveAt(1);
            Check(AuditComparison.Compare(old,current).Single(r=>r.Expected!=null&&r.Expected.Id==old.Files[1].Id).Status==AuditStatus.Missing,"Removed child appears under its original assembly");
            current=Reidentify(old);current.Files.RemoveRange(0,2);
            Check(AuditComparison.Compare(old,current).Count(r=>r.Status==AuditStatus.Missing)==2,"Missing assembly retains its missing child expectations");
            Check(AuditComparison.Compare(old,new AuditSnapshot()).Count(r=>r.Status==AuditStatus.Missing)==4,"Entire empty current federation is reported as missing");
            current=Reidentify(old);current.Files.RemoveRange(0,2);AuditFile extra=FileEntry("LANDSCAPE-TREES.nwd","C:/New/Trees.nwd");current.Files.Add(extra);current.Files.Add(FileEntry("TREES.rvt","C:/New/Trees.rvt",extra.Id));
            List<AuditRow> rows=AuditComparison.Compare(old,current);
            Check(current.Files.Count==old.Files.Count&&rows.Any(r=>r.Expected!=null&&r.Current==null)&&rows.Any(r=>r.Status==AuditStatus.New),"Same total count cannot hide removal plus addition");
            current=Reidentify(old);current.Files[0].Source="C:/Coordination/Structure.nwd";
            Check(AuditComparison.Compare(old,current).Single(r=>r.File.Id==old.Files[0].Id).Status==AuditStatus.SourceChanged,"Source relocation is loaded but explicitly flagged");
            current=Reidentify(old);current.Files[0].Name="GEN-STRUCTURE-ASM-R02.nwd";
            AuditRow renamed=AuditComparison.Compare(old,current).Single(r=>r.Expected!=null&&r.Expected.Id==old.Files[0].Id);
            Check(renamed.Status==AuditStatus.Review&&renamed.Current==null&&renamed.Candidates.Any(c=>c.File.Id==current.Files[0].Id),"Renamed file suggested without automatic acceptance");
            Check(AuditComparison.Compare(old,current).Single(r=>r.Expected!=null&&r.Expected.Id==old.Files[1].Id).Status==AuditStatus.Unverifiable,"Unresolved parent does not falsely mark its child as missing");
            AuditBaseline baseline=new AuditBaseline{Name="Complete live model",Snapshot=old};
            AuditBaseline mapped=AuditComparison.Map(baseline,current,old.Files[0].Id,current.Files[0].Id);
            Check(baseline.Snapshot.Files[0].Name.EndsWith("R01.nwd")&&mapped.Revision==2,"Mapping is a copy and preserves the original baseline");
            Check(AuditComparison.Compare(mapped.Snapshot,current).All(r=>r.Status==AuditStatus.Loaded),"Confirmed parent replacement retains child expectations");
            Reject(delegate{AuditComparison.Map(baseline,Reidentify(old),old.Files[1].Id,Reidentify(old).Files[3].Id);},"Invalid replacement cannot bypass parent ownership");
            current=Reidentify(old);
            Reject(delegate{AuditComparison.Map(baseline,current,old.Files[1].Id,current.Files[3].Id);},"A child cannot be remapped into another assembly");
            Reject(delegate{AuditComparison.Map(baseline,current,old.Files[0].Id,current.Files[2].Id);},"Loaded file already assigned elsewhere cannot be reused");
            current=Reidentify(old);current.Files.RemoveAt(1);current.Files[0].LimitedChildren=true;
            Check(AuditComparison.Compare(old,current).Single(r=>r.Expected!=null&&r.Expected.Id==old.Files[1].Id).Status==AuditStatus.Unverifiable,"Unexposed child list is uncertain rather than falsely missing or healthy");
            current=Reidentify(old);current.Files[0].Readable=false;
            Check(AuditComparison.Compare(old,current).Any(r=>r.Status==AuditStatus.Unverifiable),"Unreadable current file identity is visible");
            AuditFile d1=FileEntry("SAME.nwc","C:/A/SAME.nwc"),d2=FileEntry("SAME.nwc","C:/B/SAME.nwc");
            AuditSnapshot duplicates=new AuditSnapshot{Files=new List<AuditFile>{d1,d2}};
            Check(AuditComparison.Compare(duplicates,Reidentify(duplicates)).All(r=>r.Status==AuditStatus.Loaded),"Duplicate file names disambiguated by source");
            d2.Source=d1.Source;
            Check(AuditComparison.Compare(duplicates,new AuditSnapshot{Files=new List<AuditFile>{d1.Copy()}}).Where(r=>r.Expected!=null).All(r=>r.Current==null),"Several expected files never collapse into one current file");
            Check(AuditNames.Clean("https://acc.example/a/model.rvt?acmsession=secret#part")=="https://acc.example/a/model.rvt","Transient session query and fragment discarded");
            Check(!AuditNames.Clean("https://person:secret@acc.example/model.rvt%3Facmsession=secret").Contains("secret"),"Encoded queries and URL credentials discarded");
            Check(AuditNames.Clean("C:/Building#1/Model.rvt")=="C:/Building#1/Model.rvt","Literal hash in a local source path is retained");
            string hashUrl=AuditNames.Clean("https://acc.example/Building%231/Model.rvt?session=secret");
            Check(hashUrl.Contains("%23")&&AuditNames.Clean(hashUrl)==hashUrl,"Escaped URI path remains stable across repeated normalization");
            AuditStore store=new AuditStore(folder);store.Save(baseline);AuditBaseline saved=store.Load(new List<string>()).Single();
            Check(saved.Snapshot.Files.Count==4&&saved.Name==baseline.Name,"Complete hierarchy persists independently of NWF");
            store.Save(mapped);Check(Directory.GetFiles(folder,"*.bak").Length==1,"Updating baseline retains the previous version");
            AuditBaseline invalid=baseline.Copy();invalid.SchemaVersion=2;
            Reject(delegate{store.Save(invalid);},"Unsupported baseline schema rejected");
            Check(store.Load(new List<string>()).Single().Revision==2,"Failed save retains previous valid baseline");
            AuditSnapshot cycle=old.Copy();cycle.Files[0].ParentId=cycle.Files[1].Id;
            Reject(delegate{AuditStore.ValidateSnapshot(cycle);},"Invalid parent ordering and cycles rejected");
            File.WriteAllText(Path.Combine(folder,"corrupt.xml"),"<broken>");List<string>warnings=new List<string>();
            Check(store.Load(warnings).Count==1&&warnings.Count==1&&System.IO.File.Exists(Path.Combine(folder,"corrupt.xml")),"Corrupt list reported and retained");
            string malicious=Path.Combine(folder,"external.xml");File.WriteAllText(malicious,"<!DOCTYPE FederationBaseline [<!ENTITY x SYSTEM 'file:///not-accessed'>]><FederationBaseline><Name>&x;</Name></FederationBaseline>");
            Reject(delegate{AuditStore.Read(malicious);},"External XML entities rejected");
            string csv=Path.Combine(folder,"report.csv");AuditComparison.Export(csv,baseline,old,AuditComparison.Compare(old,Reidentify(old)));
            Check(System.IO.File.ReadAllText(csv).Contains("Parent assembly")&&System.IO.File.ReadAllLines(csv).Length==5,"Report records all file identities and their assembly context");
            AuditSnapshot large=new AuditSnapshot();for(int i=0;i<64;i++){AuditFile top=FileEntry("ASSEMBLY-"+i+".nwd","C:/Models/"+i+".nwd");large.Files.Add(top);for(int j=0;j<8;j++)large.Files.Add(FileEntry("CHILD-"+j+".rvt","C:/Models/"+i+"/"+j+".rvt",top.Id));}
            Check(AuditComparison.Compare(large,Reidentify(large)).Count(r=>r.Status==AuditStatus.Loaded)==576,"64 assemblies and 512 child files reconcile by hierarchy");
            Console.WriteLine("PASS "+checks+" federation audit checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
