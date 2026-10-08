using System;
using System.Collections.Generic;
using System.Linq;
using BIM.Tools.Models;

namespace BIM.Tools.Services
{
    public sealed class SharingExportEstimate
    {
        public double Seconds { get; private set; }
        public bool Measured { get; private set; }
        public SharingExportEstimate(double seconds,bool measured) { Seconds=Math.Max(0,seconds); Measured=measured; }
        public string Description { get { return Measured ? "Estimated time: " + Range(Seconds) : "Estimation time: measuring sample…"; } }
        public static string Range(double seconds)
        { return Format(seconds * .65) + " – " + Format(seconds * 1.65); }
        internal static string Format(double seconds)
        {
            if(seconds < 60) return Math.Max(1,(int)Math.Ceiling(seconds)) + " sec";
            if(seconds < 3600) return Math.Ceiling(seconds/60) + " min";
            return (seconds/3600).ToString("0.0") + " hr";
        }
        internal static IList<SharingSource> Sample(IList<SharingSource> sources,string scopeJson,int maximum)
        {
            int total=sources.Sum(s=>s.Elements.Count), budget=Math.Min(maximum,total);
            List<SharingSource> samples=new List<SharingSource>();
            foreach(SharingSource source in sources)
            {
                if(source.Elements.Count == 0) continue;
                // Distribute samples across the actual models, rather than assuming
                // the first pipe/fitting represents every discipline in the scope.
                int count=Math.Min(source.Elements.Count,Math.Max(1,(int)Math.Round((double)budget*source.Elements.Count/Math.Max(1,total))));
                SharingSource sample=new SharingSource(source.Name,source.Item) { ScopeJson=scopeJson };
                for(int i=0;i<count;i++) sample.Elements.Add(source.Elements[(int)((long)i*source.Elements.Count/count)]);
                samples.Add(sample);
            }
            return samples;
        }
    }
}
