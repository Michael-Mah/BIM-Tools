using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    /// <summary>IFC4 tessellated coordination geometry; only explicitly supplied items are serialized.</summary>
    internal sealed class ScopedIfcWriter : IScopedGeometryWriter
    {
        private readonly StreamWriter _writer;
        private int _next;
        private readonly int _context, _placement, _storey;
        private readonly Point3D _origin;
        private readonly double _metres;
        private readonly List<int> _products = new List<int>();
        private readonly HashSet<string> _productKeys = new HashSet<string>(StringComparer.Ordinal);
        public ScopedIfcWriter(string file, Point3D origin, double metres)
        {
            _origin = origin; _metres = metres;
            _writer = new StreamWriter(file, false, Encoding.ASCII, 65536);
            _writer.WriteLine("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');");
            _writer.WriteLine("FILE_NAME('BIM scoped coordination','" + DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "',(''),(''),'BIM Tools " + typeof(ScopedIfcWriter).Assembly.GetName().Version + "','BIM','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;");
            int zero = Add("IFCCARTESIANPOINT((0.,0.,0.))");
            int axis = Add("IFCAXIS2PLACEMENT3D(" + Ref(zero) + ",$,$)");
            _context = Add("IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-6," + Ref(axis) + ",$)");
            int unit = Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            int units = Add("IFCUNITASSIGNMENT((" + Ref(unit) + "))");
            int project = Add("IFCPROJECT(" + Text(Id("project")) + ",$,'BIM scoped coordination',$,$,$,$,(" + Ref(_context) + ")," + Ref(units) + ")");
            int originPoint = Add("IFCCARTESIANPOINT(" + Coordinate(new Point3D(origin.X*metres, origin.Y*metres, origin.Z*metres)) + ")");
            int offsetAxis = Add("IFCAXIS2PLACEMENT3D(" + Ref(originPoint) + ",$,$)");
            _placement = Add("IFCLOCALPLACEMENT($," + Ref(offsetAxis) + ")");
            int site = Add("IFCSITE(" + Text(Id("site")) + ",$,'Shared scope',$,$," + Ref(_placement) + ",$,$,.ELEMENT.,$,$,$,$,$)");
            int building = Add("IFCBUILDING(" + Text(Id("building")) + ",$,'Coordination',$,$," + Ref(_placement) + ",$,$,.ELEMENT.,$,$,$)");
            _storey = Add("IFCBUILDINGSTOREY(" + Text(Id("storey")) + ",$,'Section Box',$,$," + Ref(_placement) + ",$,$,.ELEMENT.,$)");
            Aggregate(project, site); Aggregate(site, building); Aggregate(building, _storey);
        }

        public void BeginProduct(string name,string source,string sourcePath,Autodesk.Navisworks.Api.Color color) { }
        public int Mesh(IList<Point3D> vertices)
        {
            if (vertices.Count == 0) return 0;
            List<string> points = new List<string>();
            Dictionary<string,int> indices = new Dictionary<string,int>(StringComparer.Ordinal);
            int[] vertexIndices = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                string point = LocalCoordinate(vertices[i]); int index;
                if (!indices.TryGetValue(point, out index)) { points.Add(point); index = points.Count; indices.Add(point,index); }
                vertexIndices[i] = index;
            }
            int coordinates = Add("IFCCARTESIANPOINTLIST3D((" + string.Join(",",points) + "))");
            StringBuilder faces = new StringBuilder();
            for (int i = 0; i < vertices.Count; i += 3)
            { if (i != 0) faces.Append(','); faces.AppendFormat(CultureInfo.InvariantCulture, "({0},{1},{2})", vertexIndices[i], vertexIndices[i+1], vertexIndices[i+2]); }
            return Add("IFCTRIANGULATEDFACESET(" + Ref(coordinates) + ",$,.F.,(" + faces + "),$)");
        }

        public int Line(Point3D first, Point3D last)
        {
            int a = Add("IFCCARTESIANPOINT(" + LocalCoordinate(first) + ")");
            int b = Add("IFCCARTESIANPOINT(" + LocalCoordinate(last) + ")");
            return Add("IFCPOLYLINE((" + Ref(a) + "," + Ref(b) + "))");
        }

        public int Point(Point3D point)
        { return Add("IFCCARTESIANPOINT(" + LocalCoordinate(point) + ")"); }

        public void Product(string key, string name, string source, string sourcePath, string identity,
            IList<int> meshes, IList<int> curves, IList<int> points, Autodesk.Navisworks.Api.Color color)
        {
            if (!_productKeys.Add(key)) throw new InvalidDataException("Duplicate IFC product identity: " + sourcePath);
            List<int> representations = new List<int>();
            if (meshes.Count != 0)
            {
                representations.Add(Add("IFCSHAPEREPRESENTATION(" + Ref(_context) + ",'Body','Tessellation',(" + Refs(meshes) + "))"));
                int rgb = Add("IFCCOLOURRGB($," + Real(color.R) + "," + Real(color.G) + "," + Real(color.B) + ")");
                int shading = Add("IFCSURFACESTYLESHADING(" + Ref(rgb) + ",0.)");
                int style = Add("IFCSURFACESTYLE($,.BOTH.,(" + Ref(shading) + "))");
                foreach (int mesh in meshes) Add("IFCSTYLEDITEM(" + Ref(mesh) + ",(" + Ref(style) + "),$)");
            }
            if (curves.Count != 0)
            {
                int set = Add("IFCGEOMETRICCURVESET((" + Refs(curves) + "))");
                representations.Add(Add("IFCSHAPEREPRESENTATION(" + Ref(_context) + ",'Axis','GeometricCurveSet',(" + Ref(set) + "))"));
            }
            if (points.Count != 0)
            {
                int set = Add("IFCGEOMETRICSET((" + Refs(points) + "))");
                representations.Add(Add("IFCSHAPEREPRESENTATION(" + Ref(_context) + ",'Body','GeometricSet',(" + Ref(set) + "))"));
            }
            int shape = Add("IFCPRODUCTDEFINITIONSHAPE($,$,(" + Refs(representations) + "))");
            int product = Add("IFCBUILDINGELEMENTPROXY(" + Text(Id(key)) + ",$," + Text(name) + ",'Strictly clipped coordination geometry',$," + Ref(_placement) + "," + Ref(shape) + ",$,.NOTDEFINED.)");
            _products.Add(product);
            int sourceProperty = Add("IFCPROPERTYSINGLEVALUE('Source model',$,IFCLABEL(" + Text(source) + "),$)");
            int idProperty = Add("IFCPROPERTYSINGLEVALUE('Source item path',$,IFCTEXT(" + Text(sourcePath) + "),$)");
            int identityProperty = Add("IFCPROPERTYSINGLEVALUE('Source identifier',$,IFCTEXT(" + Text(identity.Length == 0 ? "Index path fallback" : identity) + "),$)");
            int pset = Add("IFCPROPERTYSET(" + Text(Id(key+"/properties")) + ",$,'BIM Coordination',$,(" + Ref(sourceProperty) + "," + Ref(idProperty) + "," + Ref(identityProperty) + "))");
            Add("IFCRELDEFINESBYPROPERTIES(" + Text(Id(key+"/relation")) + ",$,$,$,(" + Ref(product) + ")," + Ref(pset) + ")");
        }

        public void Complete()
        {
            // Bound relation lines even for a large scoped federation.
            for (int i = 0; i < _products.Count; i += 1000)
                Add("IFCRELCONTAINEDINSPATIALSTRUCTURE(" + Text(Id("containment/" + i)) + ",$,$,$,(" + Refs(_products.Skip(i).Take(1000)) + ")," + Ref(_storey) + ")");
            _writer.WriteLine("ENDSEC;\nEND-ISO-10303-21;");
        }
        public void Dispose() { _writer.Dispose(); }
        private int Add(string entity) { int id = ++_next; _writer.WriteLine("#" + id + "=" + entity + ";"); return id; }
        private void Aggregate(int parent, int child)
        { Add("IFCRELAGGREGATES(" + Text(Id("aggregate/"+parent)) + ",$,$,$," + Ref(parent) + ",(" + Ref(child) + "))"); }
        private static string Ref(int id) { return "#" + id; }
        private static string Refs(IEnumerable<int> ids) { return string.Join(",", ids.Select(Ref)); }
        private static string Coordinate(Point3D p) { return "(" + Real(p.X) + "," + Real(p.Y) + "," + Real(p.Z) + ")"; }
        private string LocalCoordinate(Point3D p)
        { return Coordinate(new Point3D((p.X-_origin.X)*_metres, (p.Y-_origin.Y)*_metres, (p.Z-_origin.Z)*_metres)); }
        private static string Real(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Non-finite geometry coordinate.");
            string text = value.ToString("G17", CultureInfo.InvariantCulture).ToUpperInvariant();
            return text.Contains(".") || text.Contains("E") ? text : text + ".";
        }
        private static string Text(string text)
        {
            StringBuilder value = new StringBuilder("'");
            foreach (char c in text ?? string.Empty)
            {
                if (c == '\'') value.Append("''");
                else if (c == '\\') value.Append("\\\\");
                else if (c < 32 || c > 126) value.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
                else value.Append(c);
            }
            return value.Append('\'').ToString();
        }
        private static string Id(string key)
        {
            byte[] data;
            using (MD5 md5 = MD5.Create()) data = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            // Encode a 128-bit unsigned integer as the IFC 22-character base-64 GlobalId.
            char[] result = new char[22];
            for (int outIndex = 21; outIndex >= 0; outIndex--)
            {
                int remainder = 0;
                for (int i = 0; i < data.Length; i++) { int number = remainder * 256 + data[i]; data[i] = (byte)(number / 64); remainder = number % 64; }
                result[outIndex] = alphabet[remainder];
            }
            return new string(result);
        }
    }
}
