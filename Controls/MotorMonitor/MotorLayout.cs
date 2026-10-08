using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MissionPlanner.Controls.MotorMonitor
{
    // Coordinates and radii are relative to the source image width/height.
    internal sealed class MotorPosition
    {
        public int Number { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    internal sealed class MotorLayout
    {
        public int FrameClass { get; set; }
        public int FrameType { get; set; }
        public string Name { get; set; }
        public string Image { get; set; }
        public string Geometry { get; set; }
        public int[] MotorNumbers { get; set; }
        [JsonIgnore]
        public float InnerRadius { get; set; }
        [JsonIgnore]
        public float OuterRadius { get; set; }
        [JsonIgnore]
        public float OutlineRadius { get; set; }
        [JsonIgnore]
        public MotorPosition[] Motors { get; set; }

        private sealed class GeometryDefinition
        {
            public float InnerRadius { get; set; }
            public float OuterRadius { get; set; }
            public float OutlineRadius { get; set; }
            public MotorPosition[] Positions { get; set; }
        }

        private sealed class CatalogDefinition
        {
            public Dictionary<string, GeometryDefinition> Geometries { get; set; }
            public List<MotorLayout> Layouts { get; set; }
        }

        private static readonly Lazy<List<MotorLayout>> Catalog = new Lazy<List<MotorLayout>>(LoadCatalog);

        public static MotorLayout Find(int frameClass, int frameType)
        {
            return Catalog.Value.FirstOrDefault(x => x.FrameClass == frameClass && x.FrameType == frameType);
        }

        private static List<MotorLayout> LoadCatalog()
        {
            using (var stream = typeof(MotorLayout).Assembly.GetManifestResourceStream("MotorMonitor.layouts.json"))
            {
                if (stream == null)
                    throw new InvalidDataException("Каталог схем двигунів не знайдено.");
                using (var reader = new StreamReader(stream))
                {
                    var catalog = JsonConvert.DeserializeObject<CatalogDefinition>(reader.ReadToEnd());
                    var layouts = catalog?.Layouts;
                    if (catalog?.Geometries == null || layouts == null)
                        throw new InvalidDataException("Некоректний каталог схем двигунів.");
                    foreach (var layout in layouts)
                    {
                        if (string.IsNullOrEmpty(layout.Geometry) ||
                            !catalog.Geometries.TryGetValue(layout.Geometry, out var geometry) ||
                            geometry?.Positions == null || layout.MotorNumbers == null ||
                            geometry.Positions.Length != layout.MotorNumbers.Length)
                            throw new InvalidDataException("Некоректна геометрія схеми двигунів.");

                        layout.InnerRadius = geometry.InnerRadius;
                        layout.OuterRadius = geometry.OuterRadius;
                        layout.OutlineRadius = geometry.OutlineRadius;
                        layout.Motors = geometry.Positions.Select((position, index) => new MotorPosition
                        {
                            Number = layout.MotorNumbers[index], X = position.X, Y = position.Y
                        }).ToArray();
                    }
                    if (layouts == null || layouts.Count == 0 ||
                        layouts.GroupBy(x => new { x.FrameClass, x.FrameType }).Any(x => x.Count() > 1) ||
                        layouts.Any(x => string.IsNullOrEmpty(x.Image) || x.Motors == null || x.Motors.Length == 0 ||
                            x.InnerRadius <= 0 || x.OuterRadius <= x.InnerRadius || x.OutlineRadius <= x.OuterRadius ||
                            x.Motors.Select(m => m.Number).Distinct().Count() != x.Motors.Length ||
                            x.Motors.Any(m => m.Number < 1 || m.Number > 8 || m.X <= 0 || m.X >= 1 || m.Y <= 0 || m.Y >= 1)))
                        throw new InvalidDataException("Некоректний каталог схем двигунів.");
                    return layouts;
                }
            }
        }
    }
}
