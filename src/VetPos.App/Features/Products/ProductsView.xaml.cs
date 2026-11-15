using System.Windows.Controls;
using System.Windows.Input;
using VetPos.Data.Services;

namespace VetPos.App.Features.Products;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ProductsViewModel vm)
                vm.FocusName += (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    var box = FindName("NameBox") as TextBox ?? Find<TextBox>(this, "NameBox");
                    box?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ProductsViewModel { Editor: not null } vm)
            {
                vm.CloseEditorCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private static T? Find<T>(System.Windows.DependencyObject parent, string name) where T : System.Windows.FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name) return t;
            if (Find<T>(child, name) is { } found) return found;
        }
        return null;
    }

}
