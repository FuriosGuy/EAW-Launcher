using System;
using System.ComponentModel;
using System.IO;
using System.Media;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using FocLauncher.Theming;

namespace FocLauncherHost.Dialogs
{
    public partial class ExceptionWindow : INotifyPropertyChanged
    {
        private Exception _exception;

        public Exception Exception
        {
            get => _exception;
            set
            {
                if (Equals(value, _exception)) return;
                _exception = value;
                OnPropertyChanged();
            }
        }

        public ExceptionWindow(Exception exception)
        {
            InitializeComponent();
            HostWindow.WindowStyle = WindowStyle.None;
            HostWindow.AllowsTransparency = true;
            HostWindow.Background = Brushes.Transparent;
            HostWindow.ResizeMode = ResizeMode.NoResize;
            HostWindow.ShowInTaskbar = false;
            HostWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ApplyThemeResources();
            Exception = exception;
        }

        private void ApplyThemeResources()
        {
            try
            {
                var themeUri = ThemeManager.GetSavedThemeResourceUri();
                Resources.MergedDictionaries.Add(new ResourceDictionary {Source = themeUri});
            }
            catch
            {
                // The error dialog must remain usable if the theme assembly is damaged.
            }
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                HostWindow.DragMove();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            HostWindow.Close();
        }

        public override void ShowDialog()
        {
            SystemSounds.Exclamation.Play();
            base.ShowDialog();
        }

        private void OnSaveStackTrace(object sender, RoutedEventArgs e)
        {
            if (Exception?.StackTrace == null)
                return;

            var saveFileDialog = new SaveFileDialog {Title = "Save error log", Filter = "Text file (*.txt)|*.txt"};

            if (saveFileDialog.ShowDialog() != true)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("EMPIRE AT WAR Launcher error log");
            sb.AppendLine();
            sb.AppendLine(Exception.ToString());

            File.WriteAllText(saveFileDialog.FileName, sb.ToString());
            HostWindow.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }       
    }
}
