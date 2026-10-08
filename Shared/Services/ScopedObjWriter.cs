using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    // Wavefront OBJ has no unit metadata: write world coordinates in metres.
    // MTL is a required companion, containing only solid colours and no file links.
    internal sealed class ScopedObjWriter : IScopedGeometryWriter
    {
        private readonly StreamWriter _writer,_materials;
        private readonly double _metres;
        private readonly Dictionary<string,string> _styles=new Dictionary<string,string>(StringComparer.Ordinal);
        private long _vertices;
        private int _pieces;
        private string _name,_source,_path,_rgb;
        private bool _started;
        public ScopedObjWriter(string file,string materialFile,string materialReference,double metres)
        {
            _metres=metres;
            _writer=new StreamWriter(file,false,new UTF8Encoding(false),65536);
            _materials=new StreamWriter(materialFile,false,new UTF8Encoding(false),65536);
            _writer.WriteLine("# BIM strictly clipped coordination geometry. Units: metres; world coordinates.");
            _writer.WriteLine("mtllib " + materialReference);
            _materials.WriteLine("# Solid model colours; no external textures.");
        }
        public void BeginProduct(string name,string source,string path,Autodesk.Navisworks.Api.Color color)
        {
            _name=name; _source=source; _path=path; _started=false;
            _rgb=Real(color.R)+" "+Real(color.G)+" "+Real(color.B);
        }
        private void Start()
        {
            if(_started) return;
            _started=true;
            _writer.WriteLine("g " + Token(_source));
            _writer.WriteLine("o " + Token(_name) + "__path_" + Token(_path));
            string style;
            if(!_styles.TryGetValue(_rgb,out style))
            {
                style="bim_color_"+_styles.Count.ToString(CultureInfo.InvariantCulture); _styles.Add(_rgb,style);
                _materials.WriteLine("newmtl " + style + "\nKd " + _rgb + "\nKa 0 0 0\nd 1\nillum 1\n");
            }
            _writer.WriteLine("usemtl " + style + "\ns off");
        }
        public int Mesh(IList<Point3D> vertices)
        {
            Start(); long offset=_vertices;
            Dictionary<string,int> indices=new Dictionary<string,int>(StringComparer.Ordinal);
            int[] faces=new int[vertices.Count];
            for(int i=0;i<vertices.Count;i++)
            {
                string point=Coordinate(vertices[i]); int index;
                if(!indices.TryGetValue(point,out index)) { index=indices.Count+1; indices.Add(point,index); _writer.WriteLine("v " + point); _vertices++; }
                faces[i]=index;
            }
            for(int i=0;i<faces.Length;i+=3) _writer.WriteLine("f " + (offset+faces[i]) + " " + (offset+faces[i+1]) + " " + (offset+faces[i+2]));
            return ++_pieces;
        }
        public int Line(Point3D first,Point3D last)
        { Start(); _writer.WriteLine("v " + Coordinate(first)); _writer.WriteLine("v " + Coordinate(last)); _writer.WriteLine("l " + (_vertices+1) + " " + (_vertices+2)); _vertices+=2; return ++_pieces; }
        public int Point(Point3D point)
        { Start(); _writer.WriteLine("v " + Coordinate(point)); _writer.WriteLine("p " + (++_vertices)); return ++_pieces; }
        public void Product(string key,string name,string source,string sourcePath,string identity,IList<int> meshes,IList<int> curves,IList<int> points,Autodesk.Navisworks.Api.Color color) { }
        public void Complete() { _writer.Flush(); _materials.Flush(); }
        public void Dispose() { try { _writer.Dispose(); } finally { _materials.Dispose(); } }
        private string Coordinate(Point3D p) { return Real(p.X*_metres)+" "+Real(p.Y*_metres)+" "+Real(p.Z*_metres); }
        private static string Real(double value)
        { if(double.IsNaN(value)||double.IsInfinity(value)) throw new InvalidDataException("Non-finite geometry coordinate."); return value.ToString("G17",CultureInfo.InvariantCulture); }
        private static string Token(string name)
        {
            StringBuilder token=new StringBuilder();
            foreach(char c in name ?? "Unnamed") token.Append(char.IsWhiteSpace(c)||char.IsControl(c)||c=='#'||c=='/'||c=='\\'?'_':c);
            return token.Length==0 ? "Unnamed" : token.ToString();
        }
    }
}
