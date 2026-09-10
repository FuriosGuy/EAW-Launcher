using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocLauncher;
using FocLauncher.Items;
using FocLauncher.Utilities;

namespace FocLauncher.Controls
{
    internal sealed class ModOrderButton : Button
    {
        private Point _dragStartPoint;
        private bool _isPressed;

        public ModOrderButton()
        {
            Cursor = Cursors.SizeAll;
            Focusable = false;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!(DataContext is LauncherItem))
            {
                base.OnPreviewMouseLeftButtonDown(e);
                return;
            }

            _dragStartPoint = e.GetPosition(this);
            _isPressed = true;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            if (!_isPressed || e.LeftButton != MouseButtonState.Pressed || !(DataContext is LauncherItem item))
                return;

            var currentPoint = e.GetPosition(this);
            var horizontalDistance = Math.Abs(currentPoint.X - _dragStartPoint.X);
            var verticalDistance = Math.Abs(currentPoint.Y - _dragStartPoint.Y);
            if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance &&
                verticalDistance < SystemParameters.MinimumVerticalDragDistance)
                return;

            _isPressed = false;
            ReleaseMouseCapture();
            var sourceContainer = this.FindAncestor<LauncherListBoxItem>();
            var window = Window.GetWindow(this) as MainWindow;
            var pickupPoint = sourceContainer == null
                ? new Point()
                : TranslatePoint(_dragStartPoint, sourceContainer);

            if (sourceContainer != null)
                sourceContainer.IsDragSource = true;
            if (sourceContainer != null && window != null)
                window.ShowModDragPreview(sourceContainer, pickupPoint);

            GiveFeedbackEventHandler? updateDragGhost = null;
            if (window != null)
            {
                updateDragGhost = (sender, args) =>
                {
                    window.UpdateModDragPreview();
                    args.UseDefaultCursors = true;
                    args.Handled = true;
                };
                GiveFeedback += updateDragGhost;
            }

            try
            {
                DragDrop.DoDragDrop(this, new DataObject(typeof(LauncherItem), item), DragDropEffects.Move);
            }
            finally
            {
                if (updateDragGhost != null)
                    GiveFeedback -= updateDragGhost;
                if (window != null)
                    window.HideModDragPreview();
                if (sourceContainer != null)
                    sourceContainer.IsDragSource = false;
            }

            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isPressed)
            {
                _isPressed = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }

            base.OnPreviewMouseLeftButtonUp(e);
        }
    }
}
