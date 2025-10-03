using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Models;
using NovaGM.Services;
using NovaGM.Services.Packs;
using NovaGM.Services.Rules;

namespace NovaGM.Views
{
    public partial class CharacterCreatorWindow : Window
    {
        private readonly Dictionary<string, Control> _bindings = new(StringComparer.OrdinalIgnoreCase);

        public CharacterCreatorWindow()
        {
            InitializeComponent();
            try { PackLoader.LoadActiveOrDefault(); } catch { }
            BuildQuestions();
        }

        private void GenerateRandom_Click(object? sender, RoutedEventArgs e)
        {
            var character = CharacterGenerator.GenerateRandom();
            ApplyGeneratedCharacter(character);
        }

        private void GenerateFighter_Click(object? sender, RoutedEventArgs e)
        {
            var character = CharacterGenerator.GenerateForClass("fighter");
            ApplyGeneratedCharacter(character);
        }

        private void GenerateRogue_Click(object? sender, RoutedEventArgs e)
        {
            var character = CharacterGenerator.GenerateForClass("rogue");
            ApplyGeneratedCharacter(character);
        }

        private void GenerateMage_Click(object? sender, RoutedEventArgs e)
        {
            var character = CharacterGenerator.GenerateForClass("mage");
            ApplyGeneratedCharacter(character);
        }

        private void ApplyGeneratedCharacter(GeneratedCharacter character)
        {
            // Set name
            if (_bindings.TryGetValue("name", out var nameControl) && nameControl is TextBox nameBox)
            {
                nameBox.Text = character.Name;
            }

            // Set race
            if (_bindings.TryGetValue("race", out var raceControl) && raceControl is ComboBox raceCombo)
            {
                for (int i = 0; i < raceCombo.ItemCount; i++)
                {
                    if (raceCombo.Items?[i]?.ToString()?.Equals(character.Race, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        raceCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Set class
            if (_bindings.TryGetValue("class", out var classControl) && classControl is ComboBox classCombo)
            {
                for (int i = 0; i < classCombo.ItemCount; i++)
                {
                    if (classCombo.Items?[i]?.ToString()?.Equals(character.Class, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        classCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Set stats
            foreach (var stat in new[] { "str", "dex", "con", "int", "wis", "cha" })
            {
                if (_bindings.TryGetValue(stat, out var control))
                {
                    var value = character.GetBaseStat(stat); // Use base stats before racial mods
                    
                    if (control is Slider slider)
                    {
                        slider.Value = value;
                    }
                    else if (control is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Slider gridSlider)
                    {
                        gridSlider.Value = value;
                    }
                }
            }
        }

        private void BuildQuestions()
        {
            var q = PackLoader.Data.Questionnaire;
            var races = PackLoader.Data.Races.Keys.ToArray();
            var classes = PackLoader.Data.Classes.Keys.ToArray();

            var host = this.FindControl<StackPanel>("Host")!;

            foreach (var qq in q.Questions)
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(new TextBlock { Text = qq.Prompt });

                Control input;
                switch (qq.Type)
                {
                    case "select":
                        var cb = new ComboBox
                        {
                            ItemsSource = qq.Id.Equals("race", StringComparison.OrdinalIgnoreCase) ? races :
                                          qq.Id.Equals("class", StringComparison.OrdinalIgnoreCase) ? classes : qq.Options,
                            SelectedIndex = 0,
                            Width = 240
                        };
                        input = cb;
                        break;

                    case "slider":
                        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                        var sld = new Slider
                        {
                            Minimum = qq.Min,          // ints are fine (implicit to double)
                            Maximum = qq.Max,
                            Value = qq.Default,
                            IsSnapToTickEnabled = true,
                            TickFrequency = 1
                        };
                        var lbl = new TextBlock
                        {
                            Text = qq.Default.ToString(),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0)
                        };
                        sld.PropertyChanged += (_, e) =>
                        {
                            if (e.Property.Name == nameof(Slider.Value)) lbl.Text = ((int)sld.Value).ToString();
                        };
                        grid.Children.Add(sld);
                        Grid.SetColumn(lbl, 1);
                        grid.Children.Add(lbl);
                        input = grid;
                        break;

                    default:
                        input = new TextBox { Width = 280 };
                        break;
                }

                row.Children.Add(input);
                host.Children.Add(row);

                if (qq.Type == "slider")
                    _bindings[qq.Id] = ((Grid)input).Children[0]; // the Slider
                else
                    _bindings[qq.Id] = input;
            }
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

        private void Create_Click(object? sender, RoutedEventArgs e)
        {
            string text(string id) => (_bindings[id] as TextBox)?.Text?.Trim() ?? "";
            string sel(string id)
            {
                if (_bindings[id] is ComboBox cb && cb.SelectedItem is string s) return s;
                if (_bindings[id] is ComboBox cb2 && cb2.SelectedItem != null) return cb2.SelectedItem.ToString()!;
                return "";
            }
            int slid(string id) => _bindings[id] is Slider s ? (int)s.Value
                                 : _bindings[id] is Grid g && g.Children[0] is Slider s2 ? (int)s2.Value
                                 : 10;

            var name   = text("name");
            var raceId = sel("race");
            var classId= sel("class");

            int str = slid("str"), dex = slid("dex"), con = slid("con"),
                @int = slid("int"), wis = slid("wis"), cha = slid("cha");

            var race = PackLoader.Data.Races.TryGetValue(raceId, out var r) ? r : null;
            var cls  = PackLoader.Data.Classes.TryGetValue(classId, out var c) ? c : null;

            if (race != null)
            {
                str += race.Mods.TryGetValue("str", out var v1) ? v1 : 0;
                dex += race.Mods.TryGetValue("dex", out var v2) ? v2 : 0;
                con += race.Mods.TryGetValue("con", out var v3) ? v3 : 0;
                @int+= race.Mods.TryGetValue("int", out var v4) ? v4 : 0;
                wis += race.Mods.TryGetValue("wis", out var v5) ? v5 : 0;
                cha += race.Mods.TryGetValue("cha", out var v6) ? v6 : 0;
            }

            var lvl = 1;
            var prof = cls?.ProficiencyBonusByLevel is { Length: >0 } pb ? pb[Math.Clamp(lvl-1, 0, pb.Length-1)] : 2;
            int armor = 0, shield = 0, weaponAcc = 0;

            var rules = PackLoader.Data.Rules;
            var maxHP = StatCalculator.HP(lvl, con, cls?.HitDie ?? 8, rules);
            var ac    = StatCalculator.AC(dex, armor, shield, rules);
            var atk   = StatCalculator.AttackBonus(str, prof, weaponAcc, rules);
            var carry = StatCalculator.CarryCap(str, rules);

            var result = new CharacterDraft
            {
                Name = name,
                Race = raceId,
                Class = classId,
                Level = lvl,
                Stats = new Stats
                {
                    STR = str,
                    DEX = dex,
                    CON = con,
                    INT = @int,
                    WIS = wis,
                    CHA = cha
                }
            };

            try
            {
                var outDir = Paths.AppDataDir;
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "last_character.json");
                var json = JsonSerializer.Serialize(new
                {
                    name = result.Name,
                    race = result.Race,
                    @class = result.Class,
                    level = result.Level,
                    abilities = new
                    {
                        str = result.Stats.STR,
                        dex = result.Stats.DEX,
                        con = result.Stats.CON,
                        @int = result.Stats.INT,
                        wis = result.Stats.WIS,
                        cha = result.Stats.CHA
                    },
                    derived = new { maxHP, ac, attackBonus = atk, carryCap = carry }
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { /* ignore */ }

            Close(result);
        }
    }
}
