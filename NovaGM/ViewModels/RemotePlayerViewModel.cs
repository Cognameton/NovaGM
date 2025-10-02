using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaGM.Models;
using NovaGM.Services.Multiplayer;

namespace NovaGM.ViewModels
{
    public sealed class RemotePlayerViewModel : INotifyPropertyChanged
    {
        public RemotePlayerViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        private CharacterSheetViewModel? _character;
        public CharacterSheetViewModel? Character
        {
            get => _character;
            private set
            {
                if (_character != value)
                {
                    _character = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasCharacter));
                }
            }
        }

        public bool HasCharacter => Character is not null;

        public void UpdateFrom(PlayerCharacter? pc)
        {
            if (pc is null)
            {
                Character = null;
                return;
            }

            var stats = new Stats
            {
                STR = NormalizeStat(pc.STR),
                DEX = NormalizeStat(pc.DEX),
                CON = NormalizeStat(pc.CON),
                INT = NormalizeStat(pc.INT),
                WIS = NormalizeStat(pc.WIS),
                CHA = NormalizeStat(pc.CHA)
            };

            var character = new Character
            {
                Name = string.IsNullOrWhiteSpace(pc.Name) ? Name : pc.Name,
                Race = pc.Race ?? string.Empty,
                Class = pc.Class ?? string.Empty,
                Level = pc.Level ?? 1,
                Stats = stats
            };

            Character = new CharacterSheetViewModel(character);
        }

        private static int NormalizeStat(int value)
        {
            // Treat zero or negative stats as an uninitialized value and fall back to 10.
            return value > 0 ? value : 10;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
