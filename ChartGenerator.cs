using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ManufacturingKnowledgeGraph
{
    public class ChartGenerator
    {
        /// <summary>
        /// Creates a horizontal bar chart
        /// </summary>
        public static void DrawBarChart(string title, Dictionary<string, int> data, int maxBarLength = 40)
        {
            Console.WriteLine($"\n{title}");
            Console.WriteLine(new string('═', 70));

            if (!data.Any())
            {
                Console.WriteLine("  No data available");
                return;
            }

            var maxValue = data.Values.Max();
            var sortedData = data.OrderByDescending(x => x.Value).Take(10);

            foreach (var item in sortedData)
            {
                var barLength = maxValue > 0 ? (int)((double)item.Value / maxValue * maxBarLength) : 0;
                var bar = new string('█', barLength);
                var label = item.Key.Length > 20 ? item.Key.Substring(0, 17) + "..." : item.Key;
                
                Console.Write($"  {label,-20} ");
                Console.ForegroundColor = GetColorForValue(item.Value, maxValue);
                Console.Write(bar);
                Console.ResetColor();
                Console.WriteLine($" {item.Value}");
            }
        }

        /// <summary>
        /// Creates a pie chart representation
        /// </summary>
        public static void DrawPieChart(string title, Dictionary<string, int> data)
        {
            Console.WriteLine($"\n{title}");
            Console.WriteLine(new string('═', 70));

            if (!data.Any())
            {
                Console.WriteLine("  No data available");
                return;
            }

            var total = data.Values.Sum();
            
            Console.WriteLine("  ┌────────────────────────────────────────┐");
            Console.WriteLine("  │                                        │");
            
            foreach (var item in data.OrderByDescending(x => x.Value))
            {
                var percentage = (double)item.Value / total * 100;
                var barLength = (int)(percentage / 100 * 30);
                var bar = new string('█', barLength);
                
                Console.Write($"  │ {item.Key,-12} ");
                Console.ForegroundColor = GetColorForCategory(item.Key);
                Console.Write($"{bar,-30}");
                Console.ResetColor();
                Console.WriteLine($" {percentage:F1}% │");
            }
            
            Console.WriteLine("  │                                        │");
            Console.WriteLine("  └────────────────────────────────────────┘");
            Console.WriteLine($"  Total: {total}");
        }

        /// <summary>
        /// Creates a heatmap table
        /// </summary>
        public static void DrawHeatmap(string title, string[] rowLabels, string[] colLabels, int[,] data)
        {
            Console.WriteLine($"\n{title}");
            Console.WriteLine(new string('═', 70));

            // Header
            Console.Write("  Product".PadRight(20));
            foreach (var col in colLabels)
            {
                Console.Write($"{col,8}");
            }
            Console.WriteLine("  Total");
            Console.WriteLine("  " + new string('─', 60));

            // Rows
            for (int i = 0; i < rowLabels.Length; i++)
            {
                var label = rowLabels[i].Length > 18 ? rowLabels[i].Substring(0, 15) + "..." : rowLabels[i];
                Console.Write($"  {label,-18} ");

                int rowTotal = 0;
                for (int j = 0; j < colLabels.Length; j++)
                {
                    var value = data[i, j];
                    rowTotal += value;
                    
                    if (value == 0)
                    {
                        Console.Write($"{"·",8}");
                    }
                    else
                    {
                        Console.ForegroundColor = GetHeatmapColor(value);
                        Console.Write($"{value,8}");
                        Console.ResetColor();
                    }
                }
                Console.WriteLine($"{rowTotal,8}");
            }
        }

        /// <summary>
        /// Draws a network diagram showing relationships
        /// </summary>
        public static void DrawNetworkDiagram(string title, List<(string from, string to, string relation)> connections)
        {
            Console.WriteLine($"\n{title}");
            Console.WriteLine(new string('═', 70));

            if (!connections.Any())
            {
                Console.WriteLine("  No connections to display");
                return;
            }

            // Group by relation type
            var grouped = connections.GroupBy(c => c.relation).Take(3);

            foreach (var group in grouped)
            {
                Console.WriteLine($"\n  {group.Key}:");
                Console.WriteLine("  " + new string('─', 60));

                foreach (var conn in group.Take(5))
                {
                    Console.WriteLine($"    [{conn.from}]");
                    Console.WriteLine($"         │");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"         ├──{conn.relation}──>");
                    Console.ResetColor();
                    Console.WriteLine($"         │");
                    Console.WriteLine($"         └──> [{conn.to}]");
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Creates a dashboard view with multiple metrics
        /// </summary>
        public static void DrawDashboard(
            string title,
            Dictionary<string, string> keyMetrics,
            List<string> insights)
        {
            Console.Clear();
            
            // Title
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔" + new string('═', 68) + "╗");
            Console.WriteLine($"║{title.PadLeft(34 + title.Length / 2).PadRight(68)}║");
            Console.WriteLine("╚" + new string('═', 68) + "╝");
            Console.ResetColor();
            Console.WriteLine();

            // Key Metrics
            Console.WriteLine("📊 KEY METRICS");
            Console.WriteLine(new string('─', 70));
            
            var metricsList = keyMetrics.ToList();
            for (int i = 0; i < metricsList.Count; i += 2)
            {
                var metric1 = metricsList[i];
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  ▪ {metric1.Key}: ");
                Console.ResetColor();
                Console.Write($"{metric1.Value,-25}");

                if (i + 1 < metricsList.Count)
                {
                    var metric2 = metricsList[i + 1];
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"▪ {metric2.Key}: ");
                    Console.ResetColor();
                    Console.WriteLine(metric2.Value);
                }
                else
                {
                    Console.WriteLine();
                }
            }

            // Insights
            if (insights.Any())
            {
                Console.WriteLine($"\n💡 TOP INSIGHTS");
                Console.WriteLine(new string('─', 70));
                foreach (var insight in insights.Take(5))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  ✓ ");
                    Console.ResetColor();
                    Console.WriteLine(insight);
                }
            }

            Console.WriteLine();
        }

        private static ConsoleColor GetColorForValue(int value, int maxValue)
        {
            var percentage = (double)value / maxValue;
            if (percentage > 0.7) return ConsoleColor.Red;
            if (percentage > 0.4) return ConsoleColor.Yellow;
            return ConsoleColor.Green;
        }

        private static ConsoleColor GetColorForCategory(string category)
        {
            return category.ToLower() switch
            {
                var c when c.Contains("high") => ConsoleColor.Red,
                var c when c.Contains("medium") => ConsoleColor.Yellow,
                var c when c.Contains("low") => ConsoleColor.Green,
                _ => ConsoleColor.Cyan
            };
        }

        private static ConsoleColor GetHeatmapColor(int value)
        {
            if (value >= 10) return ConsoleColor.Red;
            if (value >= 5) return ConsoleColor.Yellow;
            return ConsoleColor.Green;
        }

        /// <summary>
        /// Draw a simple progress bar
        /// </summary>
        public static void DrawProgressBar(string label, int current, int total, int width = 40)
        {
            var percentage = (double)current / total;
            var filled = (int)(percentage * width);
            var bar = new string('█', filled) + new string('░', width - filled);
            
            Console.Write($"\r  {label}: [");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(bar);
            Console.ResetColor();
            Console.Write($"] {percentage:P0} ({current}/{total})");
            
            if (current >= total)
                Console.WriteLine();
        }
    }
}