using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIM.Tools.Health
{
    internal sealed class AuditListRow
    {
        internal AuditRow Evidence;
        internal string Presence
        { get { return Evidence.Status==AuditStatus.Unverifiable?"Unverified":Evidence.Expected==null?"Only B":Evidence.Current==null?"Only A":"Both"; } }
        internal string State
        { get { return Evidence.Status==AuditStatus.Review?"Possible match — review":Evidence.Status==AuditStatus.Loaded?"Same recorded file":Evidence.Status==AuditStatus.Missing?"Not matched in B":Evidence.Status==AuditStatus.New?"Not matched in A":Evidence.Label; } }
        internal string Details(AuditBaseline a,AuditBaseline b)
        {
            AuditRow r=Evidence;
            return "A: "+a+"\r\n"+a.Snapshot.DocumentPath+"\r\n"+Location(r.Expected)+"\r\n\r\nB: "+b+"\r\n"+b.Snapshot.DocumentPath+"\r\n"+Location(r.Current)
                +"\r\n\r\n"+State+"\r\n"+(r.Status==AuditStatus.Review?"Possible replacements in B (not confirmed):\r\n"+string.Join("\r\n",r.Candidates.Select(c=>c.ToString())):"")
                +"\r\nCompares recorded file identities within their parent assemblies. Geometry and current cloud versions are not compared.";
        }
        private static string Location(AuditFile file) { return file==null?"No confirmed matching entry":file.Name+"\r\n"+(string.IsNullOrEmpty(file.Source)?"Source not recorded":file.Source); }
    }
    internal static class AuditListComparison
    {
        internal static List<AuditListRow> Compare(AuditBaseline a,AuditBaseline b)
        {
            AuditStore.Validate(a);AuditStore.Validate(b);
            return AuditComparison.Compare(a.Snapshot,b.Snapshot).Select(r=>new AuditListRow{Evidence=r}).ToList();
        }
        internal static string Summary(IList<AuditListRow> rows)
        { return string.Format("{0} both · {1} only A · {2} only B · {3} unverified · {4} changed source · {5} need review",rows.Count(r=>r.Presence=="Both"),rows.Count(r=>r.Presence=="Only A"),rows.Count(r=>r.Presence=="Only B"),rows.Count(r=>r.Presence=="Unverified"),rows.Count(r=>r.Evidence.Status==AuditStatus.SourceChanged),rows.Count(r=>r.Evidence.Status==AuditStatus.Review)); }
        internal static void Export(string path,AuditBaseline a,AuditBaseline b,IList<AuditListRow> rows)
        {
            using(StreamWriter writer=new StreamWriter(path,false,new UTF8Encoding(true)))
            {
                writer.WriteLine("List A,Revision A,Document A,Captured UTC A,List B,Revision B,Document B,Captured UTC B,Presence,State,File A,Parent A,Source A,File B,Parent B,Source B,Possible matches in B,Coverage");
                foreach(AuditListRow row in rows)
                {
                    AuditRow r=row.Evidence;writer.WriteLine(string.Join(",",new[]{a.Name,a.Revision.ToString(),a.Snapshot.DocumentPath,a.Snapshot.CapturedUtc,b.Name,b.Revision.ToString(),b.Snapshot.DocumentPath,b.Snapshot.CapturedUtc,row.Presence,row.State,r.Expected==null?"":r.Expected.Name,r.Expected==null?"":AuditNames.ParentName(a.Snapshot,r.Expected),r.Expected==null?"":r.Expected.Source,r.Current==null?"":r.Current.Name,r.Current==null?"":AuditNames.ParentName(b.Snapshot,r.Current),r.Current==null?"":r.Current.Source,string.Join(" | ",r.Candidates.Select(c=>c.ToString())),(r.Expected!=null&&r.Expected.LimitedChildren)||(r.Current!=null&&r.Current.LimitedChildren)?"Embedded file coverage is limited":"Recorded source-file identities only"}.Select(AuditComparison.Csv)));
                }
            }
        }
    }
}
