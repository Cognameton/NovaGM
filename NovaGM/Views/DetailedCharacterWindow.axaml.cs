using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services.Multiplayer;

namespace NovaGM.Views
{
    public partial class DetailedCharacterWindow : Window
    {
        public DetailedCharacterWindow(PlayerCharacter character, string playerName)
        {
            InitializeComponent();
            PopulateCharacterSheet(character, playerName);
        }

        private void PopulateCharacterSheet(PlayerCharacter character, string playerName)
        {
            // Character header
            CharacterName.Text = character.Name;
            CharacterDetails.Text = $"{character.Race} {character.Class} • Level {character.Level ?? 1}";
            
            // Player info
            PlayerName.Text = playerName;
            CharacterRace.Text = character.Race;
            CharacterClass.Text = character.Class;
            CharacterLevel.Text = (character.Level ?? 1).ToString();
            
            // Ability scores
            CharacterSTR.Text = character.STR.ToString();
            CharacterDEX.Text = character.DEX.ToString();
            CharacterCON.Text = character.CON.ToString();
            CharacterINT.Text = character.INT.ToString();
            CharacterWIS.Text = character.WIS.ToString();
            CharacterCHA.Text = character.CHA.ToString();
            
            // Ability modifiers
            STRModifier.Text = $"({GetModifierText(character.STR)})";
            DEXModifier.Text = $"({GetModifierText(character.DEX)})";
            CONModifier.Text = $"({GetModifierText(character.CON)})";
            INTModifier.Text = $"({GetModifierText(character.INT)})";
            WISModifier.Text = $"({GetModifierText(character.WIS)})";
            CHAModifier.Text = $"({GetModifierText(character.CHA)})";
        }

        private static string GetModifierText(int abilityScore)
        {
            var modifier = (abilityScore - 10) / 2;
            return modifier >= 0 ? $"+{modifier}" : modifier.ToString();
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}