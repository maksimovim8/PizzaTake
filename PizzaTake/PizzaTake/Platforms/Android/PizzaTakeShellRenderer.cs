using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace PizzaTake;

/// <summary>
/// Custom Android Shell renderer ensuring bottom navigation labels (e.g., "Налаштування") fit
/// without ellipsis truncation and strictly honoring <see cref="Shell.TabBarIsVisibleProperty"/>.
/// </summary>
public sealed class PizzaTakeShellRenderer : ShellRenderer
{
    public PizzaTakeShellRenderer(Context context) : base(context)
    {
    }

    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem) =>
        new PizzaTakeBottomNavViewAppearanceTracker(this, shellItem);

    private sealed class PizzaTakeBottomNavViewAppearanceTracker : ShellBottomNavViewAppearanceTracker
    {
        private readonly ShellItem shellItem;
        private BottomNavigationView? trackedBottomView;
        private Shell? subscribedShell;

        public PizzaTakeBottomNavViewAppearanceTracker(IShellContext shellContext, ShellItem shellItem)
            : base(shellContext, shellItem)
        {
            this.shellItem = shellItem;
        }

        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);

            trackedBottomView = bottomView;
            EnsureShellSubscribed();
            UpdateBottomBarVisibility(bottomView);
            ApplyCompactLabels(bottomView);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && subscribedShell is not null)
            {
                subscribedShell.Navigated -= OnShellNavigated;
                subscribedShell = null;
            }

            trackedBottomView = null;
            base.Dispose(disposing);
        }

        private void EnsureShellSubscribed()
        {
            var currentShell = Shell.Current;
            if (currentShell is null || ReferenceEquals(subscribedShell, currentShell))
                return;

            if (subscribedShell is not null)
                subscribedShell.Navigated -= OnShellNavigated;

            subscribedShell = currentShell;
            subscribedShell.Navigated += OnShellNavigated;
        }

        private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
        {
            if (trackedBottomView is null)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (trackedBottomView is null)
                    return;

                UpdateBottomBarVisibility(trackedBottomView);
                ApplyCompactLabels(trackedBottomView);
            });
        }

        private void UpdateBottomBarVisibility(BottomNavigationView bottomView)
        {
            var currentShell = Shell.Current;
            var currentPage = currentShell?.CurrentPage;
            var hideForCurrentPage = currentPage is Views.WelcomePage
                or Views.CitySelectionPage
                or Views.PizzeriaSelectionPage
                or Views.AuthPage;

            var visible =
                !hideForCurrentPage &&
                Shell.GetTabBarIsVisible(shellItem) &&
                (shellItem.CurrentItem is null || Shell.GetTabBarIsVisible(shellItem.CurrentItem)) &&
                (currentPage is null || Shell.GetTabBarIsVisible(currentPage)) &&
                (currentShell is null || Shell.GetTabBarIsVisible(currentShell));

            var targetVisibility = visible ? ViewStates.Visible : ViewStates.Gone;
            if (bottomView.Visibility != targetVisibility)
            {
                bottomView.Visibility = targetVisibility;
                bottomView.RequestLayout();
            }
        }

        private static void ApplyCompactLabels(ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is TextView textView)
                {
                    textView.SetTextSize(ComplexUnitType.Sp, 10f);
                    textView.SetMaxLines(1);
                    textView.Ellipsize = null;
                    textView.SetPadding(2, textView.PaddingTop, 2, textView.PaddingBottom);
                }
                else if (child is ViewGroup nested)
                {
                    ApplyCompactLabels(nested);
                }
            }
        }
    }
}
