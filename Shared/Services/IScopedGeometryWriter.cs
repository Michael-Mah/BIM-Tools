using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    internal interface IScopedGeometryWriter : IDisposable
    {
        void BeginProduct(string name,string source,string sourcePath,Autodesk.Navisworks.Api.Color color);
        int Mesh(IList<Point3D> vertices);
        int Line(Point3D first,Point3D last);
        int Point(Point3D point);
        void Product(string key,string name,string source,string sourcePath,string identity,IList<int> meshes,IList<int> curves,IList<int> points,Autodesk.Navisworks.Api.Color color);
        void Complete();
    }
}
