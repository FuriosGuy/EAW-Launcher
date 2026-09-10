using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using FocLauncher.Theming;

namespace FocLauncherHost.Dialogs
{
    public class WindowHost : UserControl
    {
        protected readonly Window HostWindow;
        private readonly IntPtr _dialogWindowHandle;

        public WindowHost()
        {
            HostWindow = new Window
            {
                Title = "EMPIRE AT WAR Launcher",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize
            };
            _dialogWindowHandle = new WindowInteropHelper(HostWindow).Handle;
        }

        public virtual void ShowDialog()
        {
            SetWindowPos(_dialogWindowHandle, new IntPtr(-1), 0, 0, 0, 0, 19);
            HostWindow.Content = this;
            HostWindow.ShowDialog();
        }

        protected void UseModernChrome()
        {
            HostWindow.WindowStyle = WindowStyle.None;
            HostWindow.AllowsTransparency = true;
            HostWindow.Background = System.Windows.Media.Brushes.Transparent;
            HostWindow.ShowInTaskbar = false;
            HostWindow.UseLayoutRounding = true;
            HostWindow.SnapsToDevicePixels = true;
            HostWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        protected void LoadThemeResources()
        {
            try
            {
                var themeUri = ThemeManager.GetSavedThemeResourceUri();
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });
            }
            catch
            {
                // Updater dialogs must remain usable if saved theme resources are unavailable.
            }
        }

        protected void MoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                HostWindow.DragMove();
        }

        [DllImport("User32", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int flags);
    }
}
