using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NovaGM.ViewModels;

namespace NovaGM
{
    public partial class MainWindow : Window
    {
        private ScrollViewer? _sessionScroll;

        public MainWindow()
        {
            InitializeComponent();
            _sessionScroll = this.FindControl<ScrollViewer>("SessionScroll");
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Messages.CollectionChanged += OnMessagesChanged;
            }
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                Dispatcher.UIThread.Post(() => _sessionScroll?.ScrollToEnd(),
                    DispatcherPriority.Background);
        }
    }
}
