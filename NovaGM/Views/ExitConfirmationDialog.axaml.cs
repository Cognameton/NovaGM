using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaGM.Views
{
    public partial class ExitConfirmationDialog : Window
    {
        public bool? Result { get; private set; }

        public ExitConfirmationDialog()
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