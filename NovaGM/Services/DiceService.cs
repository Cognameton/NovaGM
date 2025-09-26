using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaGM.Services
{
    public sealed class DiceRoll
    {
        public string Expression { get; }
        public int[] Rolls { get; }
        public int Total { get; }
        public string Description { get; }

        public DiceRoll(string expression, int[] rolls, int total, string description)
        {
            Expression = expression;
            Rolls = rolls;
            Total = total;
            Description = description;
        }
    }

    /// <summary>
    /// Service for dice rolling and simulation
    /// </summary>
    public static class DiceService
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Roll dice from expression like "2d6+3", "1d20", "3d8-1"
        /// </summary>
        public static DiceRoll Roll(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return new DiceRoll("", Array.Empty<int>(), 0, "Invalid expression");

            try
            {
                var (count, sides, modifier) = ParseExpression(expression.Trim());
                var rolls = new int[count];
                
                for (int i = 0; i < count; i++)
                    rolls[i] = _random.Next(1, sides + 1);

                var total = rolls.Sum() + modifier;
                var desc = count == 1 
                    ? $"Rolled {rolls[0]}{(modifier != 0 ? $" {modifier:+0;-#}" : "")} = {total}"
                    : $"Rolled [{string.Join(", ", rolls)}]{(modifier != 0 ? $" {modifier:+0;-#}" : "")} = {total}";

                return new DiceRoll(expression, rolls, total, desc);
            }
            catch
            {
                return new DiceRoll(expression, Array.Empty<int>(), 0, "Invalid dice expression");
            }
        }

        /// <summary>
        /// Roll multiple dice expressions at once
        /// </summary>
        public static List<DiceRoll> RollMultiple(params string[] expressions)
        {
            return expressions.Select(Roll).ToList();
        }

        /// <summary>
        /// Get common dice presets
        /// </summary>
        public static Dictionary<string, string> GetPresets() => new()
        {
            ["d4"] = "1d4",
            ["d6"] = "1d6", 
            ["d8"] = "1d8",
            ["d10"] = "1d10",
            ["d12"] = "1d12",
            ["d20"] = "1d20",
            ["d100"] = "1d100",
            ["2d6"] = "2d6",
            ["3d6"] = "3d6",
            ["4d6"] = "4d6",
            ["Attack"] = "1d20",
            ["Damage"] = "1d8",
            ["Initiative"] = "1d20"
        };

        private static (int count, int sides, int modifier) ParseExpression(string expr)
        {
            // Handle simple cases like "d20", "d6"
            if (expr.StartsWith("d", StringComparison.OrdinalIgnoreCase))
                expr = "1" + expr;

            // Split on 'd'
            var parts = expr.Split('d', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new ArgumentException("Invalid format");

            var count = int.Parse(parts[0]);
            
            // Handle modifier like "6+3" or "6-2"
            var rightPart = parts[1];
            int sides, modifier = 0;
            
            if (rightPart.Contains('+'))
            {
                var modParts = rightPart.Split('+');
                sides = int.Parse(modParts[0]);
                modifier = int.Parse(modParts[1]);
            }
            else if (rightPart.Contains('-'))
            {
                var modParts = rightPart.Split('-');
                sides = int.Parse(modParts[0]);
                modifier = -int.Parse(modParts[1]);
            }
            else
            {
                sides = int.Parse(rightPart);
            }

            if (count < 1 || count > 100) throw new ArgumentException("Invalid count");
            if (sides < 2 || sides > 1000) throw new ArgumentException("Invalid sides");

            return (count, sides, modifier);
        }
    }
}