using Autodesk.Navisworks.Api;

namespace BIM.Tools.Models
{
    public sealed class DatumSource
    {
        public DatumSource(GridSystem system, bool isArchitectural, bool isStructural)
        {
            System = system;
            Name = system == null ? string.Empty : system.DisplayName;
            IsArchitectural = isArchitectural;
            IsStructural = isStructural;
            LevelCount = system == null ? 0 : system.Levels.Count;
            GridLineCount = system == null ? 0 : system.Lines.Count;
        }

        public string Name { get; private set; }

        public GridSystem System { get; private set; }

        public bool IsArchitectural { get; private set; }

        public bool IsStructural { get; private set; }

        public int LevelCount { get; private set; }

        public int GridLineCount { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class LevelDatum
    {
        public LevelDatum(string name, double worldElevation, double projectElevationMetres)
        {
            Name = name;
            WorldElevation = worldElevation;
            ProjectElevationMetres = projectElevationMetres;
        }

        public string Name { get; private set; }

        public double WorldElevation { get; private set; }

        public double ProjectElevationMetres { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class GridLineDatum
    {
        public GridLineDatum(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class GridLineGeometry
    {
        public string Name { get; set; }

        public Point3D Point { get; set; }

        public Vector3D Direction { get; set; }
    }
}
