using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaGM.Views
{
    public partial class SaveSessionDialog : Window
    {
        public bool? Result { get; private set; }

        public SaveSessionDialog()
        {
            InitializeComponent();
        }

        private void OnYesClick(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void OnNoClick(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}