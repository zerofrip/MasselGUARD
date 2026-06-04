using System.Windows;

namespace MasselGUARD
{
    /// <summary>
    /// Freezable proxy that carries a DataContext into places the visual/logical tree
    /// cannot reach (e.g. ColumnDefinition.Width inside a DataTemplate).
    /// Usage: declare in Window.Resources with Data="{Binding}", then bind with
    ///   {Binding Data.MyProp, Source={StaticResource Proxy}}
    /// </summary>
    public class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
