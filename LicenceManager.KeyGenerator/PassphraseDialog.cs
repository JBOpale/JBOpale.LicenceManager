using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LicenceManager.KeyGeneratorApp
{
    // Saisie d'une phrase de passe, avec confirmation optionnelle (creation d'une sauvegarde).
    public sealed class PassphraseDialog : Window
    {
        private readonly PasswordBox _first = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
        private readonly PasswordBox _second = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
        private readonly TextBlock _error = new TextBlock { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap };
        private readonly bool _confirm;

        public string Passphrase => _first.Password;

        private PassphraseDialog(Window owner, string title, string message, bool confirm)
        {
            _confirm = confirm;
            Owner = owner;
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "Phrase de passe :" });
            panel.Children.Add(_first);
            if (confirm)
            {
                panel.Children.Add(new TextBlock { Text = "Confirmation :" });
                panel.Children.Add(_second);
            }
            panel.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(10, 4, 10, 4) };
            ok.Click += Ok_Click;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (s, e) => _first.Focus();
        }

        // Retourne la phrase saisie, ou null si l'utilisateur annule.
        public static string Ask(Window owner, string title, string message, bool confirm)
        {
            var dialog = new PassphraseDialog(owner, title, message, confirm);
            return dialog.ShowDialog() == true ? dialog.Passphrase : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_confirm && _first.Password.Length < SigningKeyFile.MinPassphraseLength)
            {
                _error.Text = $"Au moins {SigningKeyFile.MinPassphraseLength} caractères.";
                return;
            }
            if (_confirm && _first.Password != _second.Password)
            {
                _error.Text = "Les deux saisies ne correspondent pas.";
                return;
            }
            if (_first.Password.Length == 0)
            {
                _error.Text = "Saisissez la phrase de passe.";
                return;
            }
            DialogResult = true;
        }
    }
}
