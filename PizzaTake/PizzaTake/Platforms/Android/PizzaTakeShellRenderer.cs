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

        public PizzaTakeBottomNavViewAppearanceTracker(IShellContext shellContext, ShellItem shellItem)
            : base(shellContext, shellItem)
        {
            this.shellItem = shellItem;
        }

        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);

            var currentPage = Shell.Current?.CurrentPage;
            var visible =
                Shell.GetTabBarIsVisible(shellItem) &&
                (shellItem.CurrentItem is null || Shell.GetTabBarIsVisible(shellItem.CurrentItem)) &&
                (currentPage is null || Shell.GetTabBarIsVisible(currentPage)) &&
                (Shell.Current is null || Shell.GetTabBarIsVisible(Shell.Current));

            bottomView.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
            ApplyCompactLabels(bottomView);
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
