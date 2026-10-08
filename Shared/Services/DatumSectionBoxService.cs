using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;

namespace BIM.Tools.Services
{
    public sealed class DatumSectionBoxService
    {
        private const double ParallelThreshold = 0.96;
        private const double PerpendicularThreshold = 0.28;
        private const double ViewCorrectionThresholdRadians = Math.PI / 1800.0;
        private static readonly Regex NumberPattern = new Regex(
            @"[-+]?\d+(?:[\.,]\d+)?",
            RegexOptions.CultureInvariant);

        public IList<DatumSource> GetSources(Document document)
        {
            List<DatumSource> sources = new List<DatumSource>();
            if (document == null || document.IsClear || document.Grids == null)
            {
                return sources;
            }

            foreach (GridSystem system in document.Grids.Systems)
            {
                if (system == null || string.IsNullOrWhiteSpace(system.DisplayName))
                {
                    continue;
                }

                string upper = system.DisplayName.ToUpperInvariant();
                bool architecture = upper.Contains("-M3-A-") ||
                                    upper.Contains("-M3-AR-") ||
                                    upper.Contains("ARCH");
                bool structure = upper.Contains("-M3-S-") ||
                                 upper.Contains("STRUCT");
                sources.Add(new DatumSource(system, architecture, structure));
            }

            sources.Sort(CompareSources);
            return sources;
        }

        public DatumSource GetRecommendedSource(IList<DatumSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                return null;
            }

            DatumSource best = null;
            int bestScore = int.MinValue;
            foreach (DatumSource source in sources)
            {
                string upper = source.Name.ToUpperInvariant();
                int architectureScore = upper.Contains("-M3-A-")
                    ? 400000
                    : upper.Contains("-M3-AF-") || upper.Contains("-M3-ADF-")
                        ? 220000
                        : source.IsArchitectural
                            ? 100000
                            : 0;
                if (upper.Contains("PLACEHOLDER") || upper.Contains("PENETRATION"))
                {
                    architectureScore -= 500000;
                }
                int score = architectureScore +
                            (source.LevelCount > 0 && source.GridLineCount > 1 ? 10000 : 0) +
                            Math.Min(source.LevelCount, 500) * 10 +
                            Math.Min(source.GridLineCount, 500);
                if (score > bestScore)
                {
                    best = source;
                    bestScore = score;
                }
            }

            return best;
        }

        public IList<LevelDatum> ReadLevels(Document document, DatumSource source)
        {
            List<LevelDatum> levels = new List<LevelDatum>();
            if (document == null || source == null || source.System == null)
            {
                return levels;
            }

            double toMetres = UnitConversion.ScaleFactor(document.Units, Units.Meters);
            foreach (GridLevel level in source.System.Levels)
            {
                double worldElevation = level.Elevation + source.System.Origin.Z;
                levels.Add(new LevelDatum(
                    level.DisplayName,
                    worldElevation,
                    worldElevation * toMetres));
            }

            levels.Sort(delegate(LevelDatum left, LevelDatum right)
            {
                int elevation = left.WorldElevation.CompareTo(right.WorldElevation);
                return elevation != 0
                    ? elevation
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });
            return levels;
        }

        public IList<GridLineDatum> ReadGridLines(DatumSource source)
        {
            List<GridLineDatum> lines = new List<GridLineDatum>();
            if (source == null || source.System == null)
            {
                return lines;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GridLine line in source.System.Lines)
            {
                string name = line == null ? string.Empty : line.DisplayName;
                if (!string.IsNullOrWhiteSpace(name) && names.Add(name.Trim()))
                {
                    lines.Add(new GridLineDatum(name.Trim()));
                }
            }

            lines.Sort(delegate(GridLineDatum left, GridLineDatum right)
            {
                return NaturalCompare(left.Name, right.Name);
            });
            return lines;
        }

        public LevelDatum ResolveLevel(
            Document document,
            IList<LevelDatum> loadedLevels,
            string input)
        {
            if (document == null || string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            string value = input.Trim();
            LevelDatum exact = FindExactLevel(loadedLevels, value);
            if (exact != null)
            {
                return exact;
            }

            double metres;
            if (TryReadNumber(value, out metres))
            {
                LevelDatum numeric = FindLevelByProjectElevation(loadedLevels, metres);
                if (numeric != null)
                {
                    return numeric;
                }

                double worldElevation = metres *
                    UnitConversion.ScaleFactor(Units.Meters, document.Units);
                return new LevelDatum(
                    "Manual EL " + metres.ToString("+0.###;-0.###;0.###", CultureInfo.InvariantCulture),
                    worldElevation,
                    metres);
            }

            return FindUniqueLevelMatch(loadedLevels, value);
        }

        public GridLineDatum ResolveGridLine(IList<GridLineDatum> loadedLines, string input)
        {
            if (loadedLines == null || string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            string value = input.Trim();
            foreach (GridLineDatum line in loadedLines)
            {
                if (string.Equals(line.Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }

            List<GridLineDatum> matches = loadedLines
                .Where(delegate(GridLineDatum line) { return SearchMatches(line.Name, value); })
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        public double CreateSectionBox(
            Document document,
            DatumSource gridSource,
            LevelDatum bottom,
            LevelDatum top,
            IList<GridLineDatum> boundaryLines,
            Action<string> progress)
        {
            if (document == null || document.IsClear)
            {
                throw new InvalidOperationException("Open a federated document first.");
            }
            if (gridSource == null || gridSource.System == null)
            {
                throw new InvalidOperationException("Read a Grid source first.");
            }
            if (bottom == null || top == null)
            {
                throw new InvalidOperationException("Select or enter both bottom and top elevations.");
            }
            if (top.WorldElevation <= bottom.WorldElevation)
            {
                throw new InvalidOperationException("Top elevation must be higher than bottom elevation.");
            }
            if (boundaryLines == null || boundaryLines.Count != 4)
            {
                throw new InvalidOperationException("Select four Grid boundary lines.");
            }

            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GridLineDatum line in boundaryLines)
            {
                if (line == null || !unique.Add(line.Name))
                {
                    throw new InvalidOperationException("The four Grid boundaries must be different lines.");
                }
            }

            Report(progress, "Locating the selected Grid lines...");
            BoundingBox3D bounds = FindSourceBounds(document, gridSource.Name);
            IDictionary<string, GridLineGeometry> geometry = FindGridLineGeometry(
                gridSource.System,
                unique,
                bounds,
                progress);
            if (geometry.Count != unique.Count)
            {
                List<string> missing = unique.Where(delegate(string name)
                {
                    return !geometry.ContainsKey(name);
                }).ToList();
                throw new InvalidOperationException(
                    "Navisworks could not locate an intersection for: " +
                    string.Join(", ", missing.ToArray()) +
                    ". Choose four lines that intersect another Grid family in this model.");
            }

            List<GridLineGeometry> selected = boundaryLines
                .Select(delegate(GridLineDatum line) { return geometry[line.Name]; })
                .ToList();
            Pairing pairing = FindBestPairing(selected);
            if (pairing == null)
            {
                throw new InvalidOperationException(
                    "The selected lines do not form two parallel Grid pairs. " +
                    "Choose two opposite lines from each Grid direction.");
            }

            SectionBoxGeometry sectionBox = CalculateSectionBoxGeometry(
                bottom,
                top,
                pairing);
            Report(progress, "Creating the native Section Box...");
            ApplyNativeSectionBox(document, sectionBox);

            SectionBoxScope scope;
            string reason;
            if (!SectionBoxScope.TryRead(document.ActiveView, out scope, out reason))
            {
                throw new InvalidOperationException(
                    "The Section Box was sent to Navisworks but could not be verified: " + reason);
            }
            double correctionDegrees = CorrectViewRotation(
                document,
                sectionBox,
                progress);

            // Copying a viewpoint can also copy its clipping state. Verify again
            // after the camera correction so a straightened view can never hide
            // a failed or disabled Section Box.
            if (!SectionBoxScope.TryRead(document.ActiveView, out scope, out reason))
            {
                throw new InvalidOperationException(
                    "The view was aligned but the Section Box could not be verified: " + reason);
            }
            Report(progress, "Section Box created and view aligned to the project Grid.");
            return correctionDegrees;
        }

        public static bool SearchMatches(string candidate, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string compactCandidate = Compact(candidate);
            string compactQuery = Compact(query);
            if (compactCandidate.Contains(compactQuery))
            {
                return true;
            }

            double queryNumber;
            if (!TryReadNumber(query, out queryNumber))
            {
                return false;
            }
            MatchCollection numbers = NumberPattern.Matches(candidate.Replace(',', '.'));
            foreach (Match match in numbers)
            {
                double candidateNumber;
                if (double.TryParse(
                    match.Value.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out candidateNumber) &&
                    Math.Abs(candidateNumber - queryNumber) < 0.0005)
                {
                    return true;
                }
            }
            return false;
        }

        private BoundingBox3D FindSourceBounds(Document document, string sourceName)
        {
            List<SharingSource> roots = new List<SharingSource>();
            foreach (SharingProgress step in new SharingModelDiscovery().Discover(document, roots)) { }
            string normalized = ModelNameNormalizer.NormalizeBasename(sourceName);
            IEnumerable<SharingSource> all = roots;
            foreach (SharingSource root in all)
            {
                if (!string.Equals(ModelNameNormalizer.NormalizeBasename(root.Name), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    BoundingBox3D bounds = root.Item.BoundingBox();
                    if (bounds != null && !bounds.IsEmpty)
                    {
                        return bounds;
                    }
                }
                catch
                {
                }
            }

            foreach (Model model in document.Models)
            {
                try
                {
                    BoundingBox3D modelBounds = model.RootItem.BoundingBox();
                    if (modelBounds != null && !modelBounds.IsEmpty)
                    {
                        return modelBounds;
                    }
                }
                catch
                {
                }
            }

            if (document.Models.Count == 0)
            {
                throw new InvalidOperationException("The selected Grid source has no usable model bounds.");
            }
            throw new InvalidOperationException("The selected Grid source has no usable model bounds.");
        }

        private static IDictionary<string, GridLineGeometry> FindGridLineGeometry(
            GridSystem system,
            ICollection<string> targets,
            BoundingBox3D bounds,
            Action<string> progress)
        {
            Dictionary<string, GridLineGeometry> result =
                new Dictionary<string, GridLineGeometry>(StringComparer.OrdinalIgnoreCase);
            double width = Math.Max(bounds.Size.X, 1.0);
            double depth = Math.Max(bounds.Size.Y, 1.0);
            double minX = bounds.Min.X - width * 0.03;
            double maxX = bounds.Max.X + width * 0.03;
            double minY = bounds.Min.Y - depth * 0.03;
            double maxY = bounds.Max.Y + depth * 0.03;
            int[] densities = { 18, 42, 84, 150 };

            foreach (int density in densities)
            {
                Report(progress, "Searching Grid intersections (" + density + " × " + density + ")...");
                for (int ix = 0; ix <= density && result.Count < targets.Count; ix++)
                {
                    double x = minX + (maxX - minX) * ix / density;
                    for (int iy = 0; iy <= density && result.Count < targets.Count; iy++)
                    {
                        double y = minY + (maxY - minY) * iy / density;
                        GridIntersection intersection;
                        try
                        {
                            intersection = system.ClosestIntersection(
                                new Point3D(x, y, system.Origin.Z));
                        }
                        catch
                        {
                            continue;
                        }
                        CaptureLine(intersection, true, targets, result);
                        CaptureLine(intersection, false, targets, result);
                    }
                }
                if (result.Count == targets.Count)
                {
                    break;
                }
            }

            // Datum lines are not geometry, so a local Revit grid group can sit
            // just outside the source model's geometry bounds. Search one wider
            // region at roughly three-metre spacing before reporting it missing.
            if (result.Count < targets.Count)
            {
                const int wideDensity = 320;
                double expandedMinX = bounds.Min.X - width;
                double expandedMaxX = bounds.Max.X + width;
                double expandedMinY = bounds.Min.Y - depth;
                double expandedMaxY = bounds.Max.Y + depth;
                Report(progress, "Searching local Grid groups outside model geometry...");
                for (int ix = 0; ix <= wideDensity && result.Count < targets.Count; ix++)
                {
                    double x = expandedMinX +
                        (expandedMaxX - expandedMinX) * ix / wideDensity;
                    for (int iy = 0; iy <= wideDensity && result.Count < targets.Count; iy++)
                    {
                        double y = expandedMinY +
                            (expandedMaxY - expandedMinY) * iy / wideDensity;
                        GridIntersection intersection;
                        try
                        {
                            intersection = system.ClosestIntersection(
                                new Point3D(x, y, system.Origin.Z));
                        }
                        catch
                        {
                            continue;
                        }
                        CaptureLine(intersection, true, targets, result);
                        CaptureLine(intersection, false, targets, result);
                    }
                }
            }
            return result;
        }

        private static void CaptureLine(
            GridIntersection intersection,
            bool first,
            ICollection<string> targets,
            IDictionary<string, GridLineGeometry> result)
        {
            if (intersection == null)
            {
                return;
            }
            GridLine line = first ? intersection.Line1 : intersection.Line2;
            if (line == null || result.ContainsKey(line.DisplayName) || !targets.Contains(line.DisplayName))
            {
                return;
            }
            UnitVector3D direction = first
                ? intersection.Line1Direction
                : intersection.Line2Direction;
            result.Add(line.DisplayName, new GridLineGeometry
            {
                Name = line.DisplayName,
                Point = intersection.Position,
                Direction = new Vector3D(direction.X, direction.Y, 0.0)
            });
        }

        private static Pairing FindBestPairing(IList<GridLineGeometry> lines)
        {
            int[,] options = { { 0, 1, 2, 3 }, { 0, 2, 1, 3 }, { 0, 3, 1, 2 } };
            Pairing best = null;
            double bestScore = double.MinValue;
            for (int option = 0; option < 3; option++)
            {
                GridLineGeometry a1 = lines[options[option, 0]];
                GridLineGeometry a2 = lines[options[option, 1]];
                GridLineGeometry b1 = lines[options[option, 2]];
                GridLineGeometry b2 = lines[options[option, 3]];
                Vector3D da = Normalize2D(a1.Direction);
                Vector3D da2 = Normalize2D(a2.Direction);
                Vector3D db = Normalize2D(b1.Direction);
                Vector3D db2 = Normalize2D(b2.Direction);
                double parallelA = Math.Abs(Dot2D(da, da2));
                double parallelB = Math.Abs(Dot2D(db, db2));
                double perpendicular = Math.Abs(Dot2D(da, db));
                if (parallelA < ParallelThreshold ||
                    parallelB < ParallelThreshold ||
                    perpendicular > PerpendicularThreshold)
                {
                    continue;
                }
                double score = parallelA + parallelB - perpendicular;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Pairing(a1, a2, b1, b2, da);
                }
            }
            return best;
        }

        private static SectionBoxGeometry CalculateSectionBoxGeometry(
            LevelDatum bottom,
            LevelDatum top,
            Pairing pairing)
        {
            Vector3D axisA = Normalize2D(pairing.Axis);
            Vector3D axisB = Normalize2D(pairing.B1.Direction);
            bool useAForHorizontal = Math.Abs(axisA.X) >= Math.Abs(axisB.X);
            Vector3D xAxis = ChooseCanonicalHorizontalAxis(axisA, axisB);
            Vector3D yAxis = new Vector3D(-xAxis.Y, xAxis.X, 0.0);
            GridLineGeometry horizontal1 = useAForHorizontal ? pairing.A1 : pairing.B1;
            GridLineGeometry horizontal2 = useAForHorizontal ? pairing.A2 : pairing.B2;
            GridLineGeometry vertical1 = useAForHorizontal ? pairing.B1 : pairing.A1;
            GridLineGeometry vertical2 = useAForHorizontal ? pairing.B2 : pairing.A2;
            double y1 = DotPoint(horizontal1.Point, yAxis);
            double y2 = DotPoint(horizontal2.Point, yAxis);
            double x1 = DotPoint(vertical1.Point, xAxis);
            double x2 = DotPoint(vertical2.Point, xAxis);
            double centreXLocal = (x1 + x2) * 0.5;
            double centreYLocal = (y1 + y2) * 0.5;
            double centreX = xAxis.X * centreXLocal + yAxis.X * centreYLocal;
            double centreY = xAxis.Y * centreXLocal + yAxis.Y * centreYLocal;
            double halfX = Math.Abs(x2 - x1) * 0.5;
            double halfY = Math.Abs(y2 - y1) * 0.5;
            if (halfX < 1.0e-5 || halfY < 1.0e-5)
            {
                throw new InvalidOperationException("The selected Grid boundaries create an empty plan area.");
            }

            return new SectionBoxGeometry(
                xAxis,
                yAxis,
                centreX,
                centreY,
                halfX,
                halfY,
                bottom.WorldElevation,
                top.WorldElevation);
        }

        private static void ApplyNativeSectionBox(
            Document document,
            SectionBoxGeometry geometry)
        {
            // Use the strongly typed API here. SetClippingPlanes JSON displays
            // rotation values in degrees, while Rotation3D consumes radians.
            // Passing radians directly through the JSON route made a detected
            // -4.42 degree project rotation appear as approximately -0.08/-0.09
            // in Navisworks and left the section visibly skewed.
            BoundingBox3D box = new BoundingBox3D(
                new Point3D(
                    geometry.CentreX - geometry.HalfX,
                    geometry.CentreY - geometry.HalfY,
                    geometry.Bottom),
                new Point3D(
                    geometry.CentreX + geometry.HalfX,
                    geometry.CentreY + geometry.HalfY,
                    geometry.Top));
            Rotation3D rotation = Rotation3D.CreateFromEulerAngles(
                0.0,
                0.0,
                geometry.AngleRadians);
            ClipPlaneSet clipping = new ClipPlaneSet();
            clipping.SetOrientedBox(box, rotation);
            clipping.Mode = ClipPlaneSetMode.Box;
            clipping.Enabled = true;

            Viewpoint viewpoint = document.CurrentViewpoint.CreateCopy();
            viewpoint.ClipPlanes = clipping;
            document.CurrentViewpoint.CopyFrom(viewpoint);
        }

        private static double CorrectViewRotation(
            Document document,
            SectionBoxGeometry geometry,
            Action<string> progress)
        {
            return AlignCurrentViewToProjectAxis(
                document,
                geometry.XAxis,
                progress);
        }

        private static double AlignCurrentViewToProjectAxis(
            Document document,
            Vector3D projectXAxis,
            Action<string> progress)
        {
            Vector3D xAxis = Normalize2D(projectXAxis);
            if (xAxis.X < 0.0 ||
                (Math.Abs(xAxis.X) < 1.0e-10 && xAxis.Y < 0.0))
            {
                xAxis = new Vector3D(-xAxis.X, -xAxis.Y, 0.0);
            }
            Vector3D yAxis = new Vector3D(-xAxis.Y, xAxis.X, 0.0);
            double angleRadians = Math.Atan2(xAxis.Y, xAxis.X);
            double correction = Math.Abs(angleRadians);
            if (correction < ViewCorrectionThresholdRadians)
            {
                Report(progress, "Project Grid is already aligned; no view correction is required.");
                return 0.0;
            }

            double degrees = angleRadians * 180.0 / Math.PI;
            Report(
                progress,
                "Project rotation detected: " +
                degrees.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture) +
                "°. Aligning the view...");

            Viewpoint viewpoint = document.CurrentViewpoint.CreateCopy();
            try
            {
                // AlignUp changes only camera roll. It keeps the user's current
                // look direction, projection and zoom while making the selected
                // project Grid read horizontally/vertically on screen.
                viewpoint.AlignUp(yAxis);
            }
            catch (Exception)
            {
                // A project axis can be parallel to an unusual edge-on camera.
                // Fall back to a plan view where the Grid up-vector is defined.
                viewpoint.AlignDirection(new Vector3D(0.0, 0.0, -1.0));
                viewpoint.AlignUp(yAxis);
            }
            document.CurrentViewpoint.CopyFrom(viewpoint);
            return degrees;
        }

        private static Vector3D ChooseCanonicalHorizontalAxis(
            Vector3D firstAxis,
            Vector3D secondAxis)
        {
            Vector3D first = Normalize2D(firstAxis);
            Vector3D second = Normalize2D(secondAxis);
            Vector3D result = Math.Abs(first.X) >= Math.Abs(second.X)
                ? first
                : second;
            if (result.X < 0.0 ||
                (Math.Abs(result.X) < 1.0e-10 && result.Y < 0.0))
            {
                result = new Vector3D(-result.X, -result.Y, 0.0);
            }
            return result;
        }

        private static LevelDatum FindExactLevel(IList<LevelDatum> levels, string value)
        {
            if (levels == null)
            {
                return null;
            }
            foreach (LevelDatum level in levels)
            {
                if (string.Equals(level.Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    return level;
                }
            }
            return null;
        }

        private static LevelDatum FindLevelByProjectElevation(IList<LevelDatum> levels, double metres)
        {
            if (levels == null)
            {
                return null;
            }
            LevelDatum best = null;
            double difference = double.MaxValue;
            foreach (LevelDatum level in levels)
            {
                double current = Math.Abs(level.ProjectElevationMetres - metres);
                if (current < difference)
                {
                    best = level;
                    difference = current;
                }
            }
            return difference <= 0.002 ? best : null;
        }

        private static LevelDatum FindUniqueLevelMatch(IList<LevelDatum> levels, string value)
        {
            if (levels == null)
            {
                return null;
            }
            List<LevelDatum> matches = levels
                .Where(delegate(LevelDatum level) { return SearchMatches(level.Name, value); })
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool TryReadNumber(string value, out double number)
        {
            number = 0.0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            Match match = NumberPattern.Match(value.Replace(',', '.'));
            return match.Success && double.TryParse(
                match.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static string Compact(string value)
        {
            char[] chars = value.ToUpperInvariant()
                .Where(delegate(char character)
                {
                    return char.IsLetterOrDigit(character) || character == '.' || character == ',';
                })
                .ToArray();
            return new string(chars).Replace(',', '.');
        }

        private static int CompareSources(DatumSource left, DatumSource right)
        {
            int leftRank = left.IsArchitectural ? 0 : left.IsStructural ? 1 : 2;
            int rightRank = right.IsArchitectural ? 0 : right.IsStructural ? 1 : 2;
            int rank = leftRank.CompareTo(rightRank);
            return rank != 0
                ? rank
                : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        private static int NaturalCompare(string left, string right)
        {
            double leftNumber;
            double rightNumber;
            bool hasLeft = TryReadNumber(left, out leftNumber);
            bool hasRight = TryReadNumber(right, out rightNumber);
            string leftPrefix = NumberPattern.Replace(left, string.Empty);
            string rightPrefix = NumberPattern.Replace(right, string.Empty);
            int prefix = StringComparer.OrdinalIgnoreCase.Compare(leftPrefix, rightPrefix);
            if (prefix != 0)
            {
                return prefix;
            }
            if (hasLeft && hasRight)
            {
                int numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }
            }
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static Vector3D Normalize2D(Vector3D vector)
        {
            double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
            if (length < 1.0e-10)
            {
                throw new InvalidOperationException("A selected Grid line has no plan direction.");
            }
            return new Vector3D(vector.X / length, vector.Y / length, 0.0);
        }

        private static double Dot2D(Vector3D left, Vector3D right)
        {
            return left.X * right.X + left.Y * right.Y;
        }

        private static double DotPoint(Point3D point, Vector3D vector)
        {
            return point.X * vector.X + point.Y * vector.Y;
        }

        private static void Report(Action<string> progress, string message)
        {
            if (progress != null)
            {
                progress(message);
            }
        }

        private sealed class Pairing
        {
            public Pairing(
                GridLineGeometry a1,
                GridLineGeometry a2,
                GridLineGeometry b1,
                GridLineGeometry b2,
                Vector3D axis)
            {
                A1 = a1;
                A2 = a2;
                B1 = b1;
                B2 = b2;
                Axis = axis;
            }

            public GridLineGeometry A1 { get; private set; }
            public GridLineGeometry A2 { get; private set; }
            public GridLineGeometry B1 { get; private set; }
            public GridLineGeometry B2 { get; private set; }
            public Vector3D Axis { get; private set; }
        }

        private sealed class SectionBoxGeometry
        {
            public SectionBoxGeometry(
                Vector3D xAxis,
                Vector3D yAxis,
                double centreX,
                double centreY,
                double halfX,
                double halfY,
                double bottom,
                double top)
            {
                XAxis = xAxis;
                YAxis = yAxis;
                CentreX = centreX;
                CentreY = centreY;
                HalfX = halfX;
                HalfY = halfY;
                Bottom = bottom;
                Top = top;
                AngleRadians = Math.Atan2(xAxis.Y, xAxis.X);
            }

            public Vector3D XAxis { get; private set; }
            public Vector3D YAxis { get; private set; }
            public double CentreX { get; private set; }
            public double CentreY { get; private set; }
            public double HalfX { get; private set; }
            public double HalfY { get; private set; }
            public double Bottom { get; private set; }
            public double Top { get; private set; }
            public double AngleRadians { get; private set; }
        }
    }
}
