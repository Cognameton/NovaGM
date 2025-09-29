using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services;
using NovaGM.Services.Packs;

namespace NovaGM.Views
{
    public partial class GenreSelectionWindow : Window
    {
        private GameGenre _selectedGenre = GameGenre.Fantasy;
        private readonly ObservableCollection<PreviewItem> _racesPreview = new();
        private readonly ObservableCollection<PreviewItem> _classesPreview = new();
        private readonly ObservableCollection<PreviewItem> _skillsPreview = new();
        private readonly ObservableCollection<PreviewItem> _weaponsPreview = new();
        private readonly ObservableCollection<CustomContentItem> _customContent = new();

        public GenreSelectionWindow()
        {
            InitializeComponent();
            SetupEventHandlers();
            UpdatePreview();
        }

        private void SetupEventHandlers()
        {
            FantasyRadio.Checked += (s, e) => OnGenreChanged(GameGenre.Fantasy);
            SciFiRadio.Checked += (s, e) => OnGenreChanged(GameGenre.SciFi);
            HorrorRadio.Checked += (s, e) => OnGenreChanged(GameGenre.Horror);

            AddRaceButton.Click += OnAddRaceClick;
            AddClassButton.Click += OnAddClassClick;

            OkButton.Click += OnOkClick;
            CancelButton.Click += OnCancelClick;

            RacesPreview.ItemsSource = _racesPreview;
            ClassesPreview.ItemsSource = _classesPreview;
            SkillsPreview.ItemsSource = _skillsPreview;
            WeaponsPreview.ItemsSource = _weaponsPreview;
            CustomContentList.ItemsSource = _customContent;
        }

        private void OnGenreChanged(GameGenre genre)
        {
            _selectedGenre = genre;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var tempGenreManager = new TempGenrePreview(_selectedGenre);
            var data = tempGenreManager.GetPreviewData();

            _racesPreview.Clear();
            foreach (var race in data.Races.Values)
            {
                var traits = race.Traits.Length > 0 ? string.Join(", ", race.Traits) : "No special traits";
                _racesPreview.Add(new PreviewItem { Name = race.Name, Description = traits });
            }

            _classesPreview.Clear();
            foreach (var classDef in data.Classes.Values)
            {
                var description = $"Hit Die: {classDef.HitDie}";
                _classesPreview.Add(new PreviewItem { Name = classDef.Name, Description = description });
            }

            _skillsPreview.Clear();
            foreach (var skill in data.Skills.Values)
            {
                _skillsPreview.Add(new PreviewItem { Name = skill.Name });
            }

            _weaponsPreview.Clear();
            foreach (var item in data.Items.Values.Where(i => i.Type == "weapon"))
            {
                _weaponsPreview.Add(new PreviewItem { Name = item.Name });
            }
        }

        private async void OnAddRaceClick(object? sender, RoutedEventArgs e)
        {
            var dialog = new CustomRaceDialog();
            var result = await dialog.ShowDialog<RaceDef?>(this);
            if (result != null)
            {
                _customContent.Add(new CustomContentItem { Type = "Race", Name = result.Name, Data = result });
            }
        }

        private async void OnAddClassClick(object? sender, RoutedEventArgs e)
        {
            var dialog = new CustomClassDialog();
            var result = await dialog.ShowDialog<ClassDef?>(this);
            if (result != null)
            {
                _customContent.Add(new CustomContentItem { Type = "Class", Name = result.Name, Data = result });
            }
        }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Apply genre selection
                GenreManager.SetGenre(_selectedGenre);

                // Add custom content
                foreach (var item in _customContent)
                {
                    if (item.Data is RaceDef race)
                        GenreManager.AddCustomRace(race);
                    else if (item.Data is ClassDef classDef)
                        GenreManager.AddCustomClass(classDef);
                }

                Close(_selectedGenre);
            }
            catch (Exception ex)
            {
                // Show error dialog if needed
                Close(null);
            }
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }

    public class PreviewItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class CustomContentItem
    {
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public object? Data { get; set; }
    }

    // Helper class to generate preview data without affecting global state
    internal class TempGenrePreview
    {
        private readonly GameGenre _genre;

        public TempGenrePreview(GameGenre genre)
        {
            _genre = genre;
        }

        public PackData GetPreviewData()
        {
            return _genre switch
            {
                GameGenre.Fantasy => GetFantasyPreviewData(),
                GameGenre.SciFi => GetSciFiPreviewData(),
                GameGenre.Horror => GetHorrorPreviewData(),
                _ => new PackData()
            };
        }

        private PackData GetFantasyPreviewData()
        {
            var data = new PackData();
            data.Races["human"] = new RaceDef { Name = "Human", Traits = new[] { "Versatile", "Bonus Skill" } };
            data.Races["elf"] = new RaceDef { Name = "Elf", Traits = new[] { "Keen Senses", "Fey Ancestry" } };
            data.Races["dwarf"] = new RaceDef { Name = "Dwarf", Traits = new[] { "Stout", "Stone Cunning" } };
            data.Races["halfling"] = new RaceDef { Name = "Halfling", Traits = new[] { "Lucky", "Small Size" } };
            
            data.Classes["fighter"] = new ClassDef { Name = "Fighter", HitDie = 10 };
            data.Classes["wizard"] = new ClassDef { Name = "Wizard", HitDie = 6 };
            data.Classes["rogue"] = new ClassDef { Name = "Rogue", HitDie = 8 };
            data.Classes["cleric"] = new ClassDef { Name = "Cleric", HitDie = 8 };
            
            data.Skills["arcana"] = new SkillDef { Name = "Arcana" };
            data.Skills["athletics"] = new SkillDef { Name = "Athletics" };
            data.Skills["stealth"] = new SkillDef { Name = "Stealth" };
            
            data.Items["longsword"] = new ItemDef { Name = "Longsword", Type = "weapon" };
            data.Items["bow"] = new ItemDef { Name = "Longbow", Type = "weapon" };
            data.Items["staff"] = new ItemDef { Name = "Wizard's Staff", Type = "weapon" };
            
            return data;
        }

        private PackData GetSciFiPreviewData()
        {
            var data = new PackData();
            data.Races["human"] = new RaceDef { Name = "Human", Traits = new[] { "Adaptable", "Tech Savvy" } };
            data.Races["android"] = new RaceDef { Name = "Android", Traits = new[] { "Synthetic", "No Sleep" } };
            data.Races["cyborg"] = new RaceDef { Name = "Cyborg", Traits = new[] { "Enhanced", "Tech Interface" } };
            data.Races["alien"] = new RaceDef { Name = "Alien", Traits = new[] { "Telepathic", "Alien Physiology" } };
            
            data.Classes["pilot"] = new ClassDef { Name = "Pilot", HitDie = 8 };
            data.Classes["engineer"] = new ClassDef { Name = "Engineer", HitDie = 8 };
            data.Classes["scientist"] = new ClassDef { Name = "Scientist", HitDie = 6 };
            data.Classes["soldier"] = new ClassDef { Name = "Soldier", HitDie = 10 };
            
            data.Skills["computers"] = new SkillDef { Name = "Computers" };
            data.Skills["piloting"] = new SkillDef { Name = "Piloting" };
            data.Skills["engineering"] = new SkillDef { Name = "Engineering" };
            
            data.Items["laser_pistol"] = new ItemDef { Name = "Laser Pistol", Type = "weapon" };
            data.Items["phaser"] = new ItemDef { Name = "Phaser Rifle", Type = "weapon" };
            data.Items["plasma_sword"] = new ItemDef { Name = "Plasma Sword", Type = "weapon" };
            
            return data;
        }

        private PackData GetHorrorPreviewData()
        {
            var data = new PackData();
            data.Races["human"] = new RaceDef { Name = "Human", Traits = new[] { "Adaptable", "Sanity Points" } };
            data.Races["survivor"] = new RaceDef { Name = "Survivor", Traits = new[] { "Hardy", "Paranoid" } };
            data.Races["occultist"] = new RaceDef { Name = "Occultist", Traits = new[] { "Arcane Knowledge", "Fragile Mind" } };
            
            data.Classes["investigator"] = new ClassDef { Name = "Investigator", HitDie = 8 };
            data.Classes["medic"] = new ClassDef { Name = "Medic", HitDie = 8 };
            data.Classes["survivor"] = new ClassDef { Name = "Survivor", HitDie = 10 };
            
            data.Skills["investigation"] = new SkillDef { Name = "Investigation" };
            data.Skills["medicine"] = new SkillDef { Name = "Medicine" };
            data.Skills["occult"] = new SkillDef { Name = "Occult" };
            
            data.Items["shotgun"] = new ItemDef { Name = "Shotgun", Type = "weapon" };
            data.Items["pistol"] = new ItemDef { Name = "Pistol", Type = "weapon" };
            data.Items["knife"] = new ItemDef { Name = "Combat Knife", Type = "weapon" };
            
            return data;
        }
    }
}