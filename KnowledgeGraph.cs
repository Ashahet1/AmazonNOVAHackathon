using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

namespace ManufacturingKnowledgeGraph
{
    public class Node
    {
        public string Id { get; set; }
        public string Type { get; set; } // "image", "defect", "equipment", "standard"
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class Relationship
    {
        public string FromNodeId { get; set; }
        public string ToNodeId { get; set; }
        public string RelationType { get; set; } // "has_defect", "requires_equipment", etc.
        public double Confidence { get; set; }
    }

    public class KnowledgeGraph
    {
        private List<Node> nodes = new();
        private List<Relationship> relationships = new();

        // Public read-only accessors
        public IReadOnlyList<Node> AllNodes => nodes;
        public IReadOnlyList<Relationship> AllRelationships => relationships;

        public Node? GetNodeById(string id) => nodes.FirstOrDefault(n => n.Id == id);

        public void AddNode(Node node)
        {
            if (!nodes.Any(n => n.Id == node.Id))
                nodes.Add(node);
        }

        public void AddRelationship(Relationship rel)
        {
            relationships.Add(rel);
        }

        public List<Node> GetNodesByType(string type)
        {
            return nodes.Where(n => n.Type == type).ToList();
        }

        public List<Node> GetRelatedNodes(string nodeId, string relationType)
        {
            var relatedIds = relationships
                .Where(r => r.FromNodeId == nodeId && r.RelationType == relationType)
                .Select(r => r.ToNodeId);
            
            return nodes.Where(n => relatedIds.Contains(n.Id)).ToList();
        }

        public List<(Node, Node, string)> FindSimilarDefectsAcrossProducts()
        {
            var results = new List<(Node, Node, string)>();
            var defectNodes = nodes.Where(n => n.Type == "defect").ToList();

            for (int i = 0; i < defectNodes.Count; i++)
            {
                for (int j = i + 1; j < defectNodes.Count; j++)
                {
                    var defect1 = defectNodes[i];
                    var defect2 = defectNodes[j];
                    
                    // Get products for each defect
                    var product1 = GetProductForDefect(defect1.Id);
                    var product2 = GetProductForDefect(defect2.Id);

                    // If different products but similar defect name
                    if (product1 != product2 && 
                        IsSimilarDefect(defect1.Properties["name"].ToString(), 
                                       defect2.Properties["name"].ToString()))
                    {
                        results.Add((defect1, defect2, "similar_defect_type"));
                    }
                }
            }

            return results;
        }

        private string GetProductForDefect(string defectId)
        {
            var imageNode = relationships
                .Where(r => r.ToNodeId == defectId && r.RelationType == "has_defect")
                .Select(r => nodes.FirstOrDefault(n => n.Id == r.FromNodeId))
                .FirstOrDefault();

            return imageNode?.Properties.ContainsKey("product") == true 
                ? imageNode.Properties["product"].ToString() 
                : "unknown";
        }

        private bool IsSimilarDefect(string defect1, string defect2)
        {
            // Simple similarity check
            var keywords = new[] { "scratch", "crack", "dent", "hole", "contamination", "bent", "broken", "color" };
            
            foreach (var keyword in keywords)
            {
                if (defect1.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    defect2.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }

        public void PrintGraph()
        {
            Console.WriteLine($"\n📊 Knowledge Graph Statistics:");
            Console.WriteLine($"   Total Nodes: {nodes.Count}");
            Console.WriteLine($"   Total Relationships: {relationships.Count}");
            Console.WriteLine($"   Images: {nodes.Count(n => n.Type == "image")}");
            Console.WriteLine($"   Defects: {nodes.Count(n => n.Type == "defect")}");
            Console.WriteLine($"   Equipment: {nodes.Count(n => n.Type == "equipment")}");
        }

        public List<Node> QueryDefectsByProduct(string productName)
        {
            var imageNodes = nodes
                .Where(n => n.Type == "image" && 
                           n.Properties.ContainsKey("product") &&
                           n.Properties["product"].ToString().Contains(productName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var defectIds = relationships
                .Where(r => imageNodes.Select(img => img.Id).Contains(r.FromNodeId) && 
                           r.RelationType == "has_defect")
                .Select(r => r.ToNodeId)
                .Distinct();

            return nodes.Where(n => defectIds.Contains(n.Id)).ToList();
        }

        public Dictionary<string, List<string>> GetEquipmentRecommendations()
        {
            var recommendations = new Dictionary<string, List<string>>();

            var defectNodes = nodes.Where(n => n.Type == "defect").ToList();

            foreach (var defect in defectNodes)
            {
                var defectName = defect.Properties["name"].ToString();
                var equipmentIds = relationships
                    .Where(r => r.FromNodeId == defect.Id && r.RelationType == "requires_equipment")
                    .Select(r => r.ToNodeId);

                var equipment = nodes
                    .Where(n => equipmentIds.Contains(n.Id))
                    .Select(n => n.Properties["name"].ToString())
                    .ToList();

                if (equipment.Any())
                {
                    if (!recommendations.ContainsKey(defectName))
                        recommendations[defectName] = new List<string>();
                    
                    recommendations[defectName].AddRange(equipment);
                }
            }

            return recommendations;
        }

        // Add to KnowledgeGraph.cs class

/// <summary>
/// Get defect frequency distribution
/// </summary>
public Dictionary<string, int> GetDefectFrequency()
{
    var defectNodes = nodes.Where(n => n.Type == "defect");
    return defectNodes
        .GroupBy(d => d.Properties["name"].ToString())
        .ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Get severity distribution
/// </summary>
public Dictionary<string, int> GetSeverityDistribution()
{
    var defectNodes = nodes.Where(n => n.Type == "defect");
    return defectNodes
        .GroupBy(d => d.Properties["severity"].ToString())
        .ToDictionary(
            g => char.ToUpper(g.Key[0]) + g.Key.Substring(1) + " Severity",
            g => g.Count()
        );
}

/// <summary>
/// Get product defect counts
/// </summary>
public Dictionary<string, int> GetProductDefectCounts()
{
    var imageNodes = nodes.Where(n => n.Type == "image");
    var products = imageNodes
        .Select(img => img.Properties["product"].ToString())
        .Distinct();

    var result = new Dictionary<string, int>();
    foreach (var product in products)
    {
        result[product] = QueryDefectsByProduct(product).Count;
    }

    return result;
}

/// <summary>
/// Get equipment usage statistics
/// </summary>
public Dictionary<string, int> GetEquipmentUsage()
{
    var result = new Dictionary<string, int>();
    var equipmentNodes = nodes.Where(n => n.Type == "equipment");

    foreach (var equipment in equipmentNodes)
    {
        var usageCount = relationships.Count(r => 
            r.ToNodeId == equipment.Id && 
            r.RelationType == "requires_equipment");
        
        result[equipment.Properties["name"].ToString()] = usageCount;
    }

    return result.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
}

/// <summary>
/// Get product quality heatmap data
/// </summary>
public (string[] products, string[] severities, int[,] data) GetQualityHeatmap()
{
    var imageNodes = nodes.Where(n => n.Type == "image");
    var products = imageNodes
        .Select(img => img.Properties["product"].ToString())
        .Distinct()
        .OrderBy(p => p)
        .ToArray();

    var severities = new[] { "Low", "Medium", "High" };
    var data = new int[products.Length, severities.Length];

    for (int i = 0; i < products.Length; i++)
    {
        var productDefects = QueryDefectsByProduct(products[i]);
        
        for (int j = 0; j < severities.Length; j++)
        {
            data[i, j] = productDefects.Count(d => 
                d.Properties["severity"].ToString().Equals(severities[j], StringComparison.OrdinalIgnoreCase));
        }
    }

    return (products, severities, data);
}

/// <summary>
/// Generate rich local insights from graph data (no API calls needed)
/// </summary>
public List<string> GenerateInsights()
{
    var insights = new List<string>();

    var defectFreq = GetDefectFrequency();
    var severityDist = GetSeverityDistribution();
    var productDefects = GetProductDefectCounts();
    var equipmentUsage = GetEquipmentUsage();
    var similarities = FindSimilarDefectsAcrossProducts();
    var defects = GetNodesByType("defect");
    var images = GetNodesByType("image");

    // ── Insight 1: Most common defect + its spread ──
    if (defectFreq.Any())
    {
        var top = defectFreq.OrderByDescending(x => x.Value).First();
        var productsWithThisDefect = defects
            .Where(d => d.Properties.ContainsKey("name") && d.Properties["name"]?.ToString() == top.Key)
            .Select(d => d.Properties.ContainsKey("product") ? d.Properties["product"]?.ToString() : "")
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();
        var pct = images.Count > 0 ? (double)top.Value / images.Count * 100 : 0;
        insights.Add($"Most common defect: '{top.Key}' ({top.Value} instances, {pct:F0}% of images) — affects {productsWithThisDefect.Count} product(s): {string.Join(", ", productsWithThisDefect.Take(5))}");
    }

    // ── Insight 2: Cross-product knowledge transfer potential ──
    if (similarities.Any())
    {
        var uniqueDefectTypes = similarities
            .Select(s => s.Item1.Properties.ContainsKey("name") ? s.Item1.Properties["name"]?.ToString() ?? "" : "")
            .Where(n => n.Length > 0)
            .Distinct().ToList();
        insights.Add($"Cross-product patterns: {similarities.Count} connections across {uniqueDefectTypes.Count} shared defect type(s) ({string.Join(", ", uniqueDefectTypes.Take(4))}) — enables inspection knowledge transfer between product lines");
    }

    // ── Insight 3: Severity risk analysis ──
    var highKey = severityDist.Keys.FirstOrDefault(k => k.StartsWith("High"));
    var medKey = severityDist.Keys.FirstOrDefault(k => k.StartsWith("Medium"));
    var lowKey = severityDist.Keys.FirstOrDefault(k => k.StartsWith("Low"));
    int highCount = highKey != null && severityDist.ContainsKey(highKey) ? severityDist[highKey] : 0;
    int medCount = medKey != null && severityDist.ContainsKey(medKey) ? severityDist[medKey] : 0;
    int lowCount = lowKey != null && severityDist.ContainsKey(lowKey) ? severityDist[lowKey] : 0;
    int totalDefects = highCount + medCount + lowCount;
    if (totalDefects > 0)
    {
        var highPct = (double)highCount / totalDefects * 100;
        var criticalProducts = new List<string>();
        var (products, _, heatmapData) = GetQualityHeatmap();
        for (int i = 0; i < products.Length; i++)
        {
            if (heatmapData[i, 2] > 0) // High severity column
                criticalProducts.Add($"{products[i]} ({heatmapData[i, 2]})");
        }
        insights.Add($"Severity distribution: {highCount} high ({highPct:F0}%), {medCount} medium, {lowCount} low — products needing priority review: {(criticalProducts.Any() ? string.Join(", ", criticalProducts) : "none")}");
    }

    // ── Insight 4: Equipment investment priority ──
    if (equipmentUsage.Any())
    {
        var sorted = equipmentUsage.OrderByDescending(x => x.Value).ToList();
        var topEquip = sorted.First();
        var defectCount = defects.Count;
        var coverage = defectCount > 0 ? (double)topEquip.Value / defectCount * 100 : 0;
        insights.Add($"Equipment priority: '{topEquip.Key}' covers {coverage:F0}% of all defects ({topEquip.Value}/{defectCount}) — {(sorted.Count > 1 ? $"followed by '{sorted[1].Key}' at {(defectCount > 0 ? (double)sorted[1].Value / defectCount * 100 : 0):F0}%" : "only equipment type detected")}");
    }

    // ── Insight 5: Product complexity & standardization ──
    if (productDefects.Any())
    {
        var sorted = productDefects.OrderByDescending(x => x.Value).ToList();
        var avg = sorted.Average(x => x.Value);
        var mostComplex = sorted.First();
        var simplest = sorted.Last();
        insights.Add($"Product complexity: '{mostComplex.Key}' has most defect types ({mostComplex.Value}), '{simplest.Key}' has fewest ({simplest.Value}), average {avg:F1} per product — {(sorted.Count(x => x.Value > avg) > sorted.Count / 2 ? "most products above average, review needed" : "defects well-distributed across products")}");
    }

    // ── Insight 6: Defect diversity index ──
    if (defectFreq.Count > 1 && defects.Count > 0)
    {
        // Simple diversity: unique types vs total instances
        var diversity = (double)defectFreq.Count / defects.Count;
        var repeated = defectFreq.Count(x => x.Value > 1);
        insights.Add($"Defect diversity: {defectFreq.Count} unique types across {defects.Count} instances (diversity index: {diversity:F2}) — {repeated} type(s) appear more than once, indicating systemic patterns worth investigating");
    }

    // ── Insight 7: Graph connectivity ──
    var relTypes = relationships.GroupBy(r => r.RelationType).ToDictionary(g => g.Key, g => g.Count());
    if (relTypes.Any())
    {
        var totalRels = relationships.Count;
        var relSummary = string.Join(", ", relTypes.OrderByDescending(x => x.Value).Select(x => $"{x.Key}: {x.Value}"));
        insights.Add($"Knowledge graph density: {nodes.Count} nodes, {totalRels} relationships ({relSummary}) — {(totalRels > nodes.Count * 2 ? "highly connected graph, rich for inference" : "moderately connected, growing with more data")}");
    }

    return insights;
    }
    /// <summary>
/// Save knowledge graph to JSON file
/// </summary>
public void SaveToFile(string filename = "knowledge_graph.json")
{
    var data = new
    {
        nodes = this.nodes,
        relationships = this.relationships,
        saved_at = DateTime.Now,
        version = "1.0"
    };

    var options = new JsonSerializerOptions 
    { 
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    var json = JsonSerializer.Serialize(data, options);
    File.WriteAllText(filename, json);
    
    Console.WriteLine($"\n💾 Knowledge graph saved to: {filename}");
    Console.WriteLine($"   Nodes: {nodes.Count}, Relationships: {relationships.Count}");
}

/// <summary>
/// Load knowledge graph from JSON file
/// </summary>
public static KnowledgeGraph LoadFromFile(string filename = "knowledge_graph.json")
{
    if (!File.Exists(filename))
    {
        return null;
    }

    try
    {
        var json = File.ReadAllText(filename);
        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var data = JsonSerializer.Deserialize<GraphData>(json, options);
        
        var graph = new KnowledgeGraph();
        graph.nodes = data.Nodes.ToList();
        graph.relationships = data.Relationships.ToList();

        Console.WriteLine($"\n✅ Knowledge graph loaded from: {filename}");
        Console.WriteLine($"   Nodes: {graph.nodes.Count}, Relationships: {graph.relationships.Count}");
        Console.WriteLine($"   Last saved: {data.SavedAt}");

        return graph;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Error loading graph: {ex.Message}");
        return null;
    }
}

/// <summary>
/// Check if cached graph exists and is recent
/// </summary>
public static bool CacheExists(string filename = "knowledge_graph.json")
{
    return File.Exists(filename);
}

// Helper class for deserialization
private class GraphData
{
    public List<Node> Nodes { get; set; }
    public List<Relationship> Relationships { get; set; }
    public DateTime SavedAt { get; set; }
    public string Version { get; set; }
}
    }
}