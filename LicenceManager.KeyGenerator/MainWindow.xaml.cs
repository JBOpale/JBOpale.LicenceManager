using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace LicenceManager.KeyGeneratorApp
{
    public partial class MainWindow : Window
    {
        private readonly LicenceKeyService _service = new LicenceKeyService();
        private ProductCatalog _catalog;
        private readonly Dictionary<ProductModule, CheckBox> _moduleBoxes = new Dictionary<ProductModule, CheckBox>();

        // Saisie des champs personnalises du profil courant : champ -> lecture de la saisie
        // au format attendu par ProductField.TryApply.
        private readonly List<KeyValuePair<ProductField, Func<string>>> _fieldInputs = new List<KeyValuePair<ProductField, Func<string>>>();

        // Source de verite du masque : les cases n'en couvrent que les bits connus, les autres
        // bits (modules futurs, ex. apres "Tout cocher") sont conserves tels quels.
        private ulong _features;
        private bool _updatingFeatures;

        public MainWindow()
        {
            InitializeComponent();

            ExpirationDatePicker.SelectedDate = DateTime.Today.AddYears(1);

            ProductCatalog catalog = _catalog = ProductCatalog.Load(ProductCatalog.DefaultFilePath);
            ProfileComboBox.ItemsSource = catalog.Profiles;
            ProfileComboBox.SelectedIndex = 0;
            if (catalog.Warnings.Count > 0)
                MessageBox.Show(this, string.Join("\n", catalog.Warnings), "Profils produit", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (File.Exists(LicenceKeyService.DefaultKeyFilePath))
            {
                try
                {
                    _service.LoadKeyFile(LicenceKeyService.DefaultKeyFilePath, AskPassphrase);
                }
                catch (Exception ex)
                {
                    KeyStatusText.Text = "Échec du chargement automatique : " + ex.Message;
                    return;
                }
            }
            UpdateKeyStatus();
        }

        private void NewKeyPairButton_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsLoaded)
            {
                var confirm = MessageBox.Show(
                    "Une paire de clés est déjà chargée. En générer une nouvelle rendra invalides toutes les licences déjà émises avec l'ancienne (la nouvelle clé publique ne correspondra plus). Continuer ?",
                    "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Fichier de travail de la nouvelle paire de clés (protégé pour votre compte Windows)",
                Filter = KeyFileFilter,
                FileName = Path.GetFileName(LicenceKeyService.DefaultKeyFilePath),
                InitialDirectory = Path.GetDirectoryName(LicenceKeyService.DefaultKeyFilePath),
            };
            Directory.CreateDirectory(dialog.InitialDirectory ?? ".");

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                _service.CreateNewKeyPair();
                _service.SaveKeyFile(dialog.FileName);
                UpdateKeyStatus();
            }
            catch (Exception ex)
            {
                ShowError("Impossible de générer la paire de clés", ex);
                return;
            }

            MessageBox.Show(this,
                "Nouvelle paire de clés générée et enregistrée.\n\n" +
                "Ce fichier n'est lisible que par votre compte Windows sur ce poste. Exportez maintenant " +
                "une sauvegarde protégée par phrase de passe et conservez-la hors de ce poste : sans elle, " +
                "la perte du poste empêcherait d'émettre de nouvelles licences.",
                "Paire de clés créée", MessageBoxButton.OK, MessageBoxImage.Information);
            ExportBackupButton_Click(sender, e);
        }

        private void LoadKeyPairButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Charger une paire de clés (fichier de travail ou sauvegarde)",
                Filter = KeyFileFilter + "|Tous les fichiers|*.*",
                InitialDirectory = Path.GetDirectoryName(LicenceKeyService.DefaultKeyFilePath),
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                if (!_service.LoadKeyFile(dialog.FileName, AskPassphrase)) return;
                UpdateKeyStatus();
            }
            catch (Exception ex)
            {
                ShowError("Impossible de charger ce fichier de clé", ex);
                return;
            }

            // Sauvegarde restauree sur un poste : proposer d'en faire le fichier de travail, pour
            // ne plus avoir a saisir la phrase de passe a chaque lancement.
            if (SigningKeyFile.ReadProtection(dialog.FileName) == SigningKeyFile.Passphrase)
            {
                var answer = MessageBox.Show(this,
                    "Enregistrer cette paire comme fichier de travail de ce poste (protégé par votre compte Windows, chargé automatiquement au démarrage) ?",
                    "Fichier de travail", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                try
                {
                    _service.SaveKeyFile(LicenceKeyService.DefaultKeyFilePath);
                    UpdateKeyStatus();
                }
                catch (Exception ex)
                {
                    ShowError("Impossible d'enregistrer le fichier de travail", ex);
                }
            }
        }

        private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_service.IsLoaded) return;

            var dialog = new SaveFileDialog
            {
                Title = "Sauvegarde de la paire de clés (à conserver hors de ce poste)",
                Filter = KeyFileFilter,
                FileName = "licence-signing-key.backup.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            string passphrase = PassphraseDialog.Ask(this, "Phrase de passe de la sauvegarde",
                "Choisissez une phrase de passe longue et notez-la en lieu sûr : elle sera demandée pour restaurer la sauvegarde.",
                confirm: true);
            if (passphrase == null) return;

            try
            {
                _service.ExportBackup(dialog.FileName, passphrase);
                MessageBox.Show(this, "Sauvegarde enregistrée.", "Sauvegarde", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Impossible d'enregistrer la sauvegarde", ex);
            }
        }

        private const string KeyFileFilter = "Paire de clés LicenceManager (*.json)|*.json";

        private string AskPassphrase() =>
            PassphraseDialog.Ask(this, "Phrase de passe", "Ce fichier est une sauvegarde protégée par phrase de passe.", confirm: false);

        private void UpdateKeyStatus()
        {
            KeyStatusText.Text = _service.IsLoaded
                ? $"Paire de clés chargée (empreinte {_service.Fingerprint})" + (_service.LoadedPath != null ? " : " + _service.LoadedPath : "")
                : "Aucune paire de clés chargée.";
            GenerateButton.IsEnabled = _service.IsLoaded;
            ExportBackupButton.IsEnabled = _service.IsLoaded;
            CopyPublicKeyButton.IsEnabled = _service.IsLoaded;
            PublicKeyTextBox.Text = _service.PublicKeyBase64 ?? string.Empty;
        }

        // Adapte le formulaire au profil produit : ID, zone Features (cases / hex seul / absente),
        // taille max du client.
        private void ApplyProfile(ProductProfile profile)
        {
            _service.Profile = profile;
            ProductIdTextBox.Text = profile.ProductId.ToString();

            FeaturesLabel.Visibility = FeaturesPanel.Visibility = profile.HasFeatures ? Visibility.Visible : Visibility.Collapsed;
            ModulesArea.Visibility = profile.HasModules ? Visibility.Visible : Visibility.Collapsed;
            FeaturesLabel.Content = profile.HasModules ? "Modules (Features)" : "Features";
            FeaturesHelpText.Text = profile.HasModules
                ? "Un bit par module (voir products.json). « Tout cocher » = 0xFFFFFFFFFFFFFFFF, qui couvre aussi les modules futurs."
                : "Masque 64 bits en hexadécimal (ex. 0x000000000000000B).";

            BuildModuleCheckBoxes(profile.HasModules ? profile.Modules : new List<ProductModule>());
            BuildFieldInputs(profile);
            SetFeatures(0);
            UpdateClientLengthText();
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileComboBox.SelectedItem is ProductProfile profile) ApplyProfile(profile);
        }

        private void BuildFieldInputs(ProductProfile profile)
        {
            _fieldInputs.Clear();
            FieldsPanel.Children.Clear();
            FieldsLabel.Visibility = FieldsPanel.Visibility = profile.HasFields ? Visibility.Visible : Visibility.Collapsed;
            if (!profile.HasFields) return;

            foreach (ProductField field in profile.Fields.OrderBy(f => f.Id))
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"champ {field.Id}, type {field.Type.ToString().ToLowerInvariant()}",
                });

                Func<string> read;
                switch (field.Type)
                {
                    case ProductFieldType.Bool:
                        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(box);
                        read = () => box.IsChecked == true ? "true" : "false";
                        break;
                    case ProductFieldType.Date:
                        var picker = new DatePicker { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
                        row.Children.Add(picker);
                        read = () => picker.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        break;
                    default:
                        var input = new TextBox
                        {
                            Width = field.Type == ProductFieldType.Text ? 260 : 190,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            FontFamily = field.Type == ProductFieldType.Mask ? new FontFamily("Consolas") : this.FontFamily,
                            ToolTip = field.Type == ProductFieldType.Mask ? "Hexadécimal, ex. 0x000000000000000B"
                                    : field.Type == ProductFieldType.Text ? $"{LicenceFields.TextMaxBytes} octets UTF-8 au maximum" : "Entier",
                        };
                        row.Children.Add(input);
                        read = () => input.Text;
                        break;
                }
                FieldsPanel.Children.Add(row);
                _fieldInputs.Add(new KeyValuePair<ProductField, Func<string>>(field, read));
            }
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Laisser vide pour ne pas inclure l'information dans la clé.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        private void BuildModuleCheckBoxes(IEnumerable<ProductModule> modules)
        {
            _moduleBoxes.Clear();
            ModulesPanel.Children.Clear();
            foreach (ProductModule module in modules)
            {
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = module.Name }, // TextBlock : un CheckBox avale le "_" (touche d'acces)
                    ToolTip = $"bit {module.Bit} (0x{module.Mask:X})",
                    Margin = new Thickness(0, 1, 8, 1),
                };
                ulong bit = module.Mask;
                RoutedEventHandler onChanged = (s, e) =>
                {
                    if (_updatingFeatures) return;
                    SetFeatures(((CheckBox)s).IsChecked == true ? _features | bit : _features & ~bit);
                };
                box.Checked += onChanged;
                box.Unchecked += onChanged;
                _moduleBoxes[module] = box;
                ModulesPanel.Children.Add(box);
            }
        }

        // Met a jour cases, champ hexadecimal et affichage decimal depuis le masque.
        private void SetFeatures(ulong features, bool updateHexText = true)
        {
            _updatingFeatures = true;
            try
            {
                _features = features;
                foreach (var kv in _moduleBoxes)
                    kv.Value.IsChecked = (features & kv.Key.Mask) != 0;
                if (updateHexText) FeaturesTextBox.Text = FeatureMask.FormatHex(features);
                FeaturesDecimalText.Text = "Décimal (non signé) : " + FeatureMask.FormatDecimal(features);
                FeaturesDecimalText.Foreground = Brushes.Black;
            }
            finally
            {
                _updatingFeatures = false;
            }
        }

        private void CheckAllButton_Click(object sender, RoutedEventArgs e) => SetFeatures(FeatureMask.AllFeatures);

        private void CheckNoneButton_Click(object sender, RoutedEventArgs e) => SetFeatures(0);

        private void FeaturesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Peut etre declenche pendant InitializeComponent, avant la creation des controles suivants.
            if (_updatingFeatures || FeaturesDecimalText == null) return;
            if (FeatureMask.TryParseHex(FeaturesTextBox.Text, out ulong features))
            {
                SetFeatures(features, updateHexText: false);
            }
            else
            {
                FeaturesDecimalText.Text = "Masque hexadécimal invalide (16 chiffres hexa max).";
                FeaturesDecimalText.Foreground = Brushes.Red;
            }
        }

        private void NeverExpiresCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ExpirationDatePicker.IsEnabled = NeverExpiresCheckBox.IsChecked != true;
        }

        private void BindMachineCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            MachineCodeTextBox.IsEnabled = BindMachineCheckBox.IsChecked == true;
            UpdateMachineCodeStatus();
        }

        private void MachineCodeTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateMachineCodeStatus();

        // Le code poste porte un CRC : une faute de frappe est signalee des la saisie.
        private void UpdateMachineCodeStatus()
        {
            if (MachineCodeStatusText == null) return; // pendant InitializeComponent
            if (BindMachineCheckBox.IsChecked != true || string.IsNullOrWhiteSpace(MachineCodeTextBox.Text))
            {
                MachineCodeStatusText.Text = string.Empty;
                return;
            }
            bool ok = MachineCode.TryNormalize(MachineCodeTextBox.Text, out _);
            MachineCodeStatusText.Text = ok ? "✓ code valide" : "✗ code invalide";
            MachineCodeStatusText.Foreground = ok ? Brushes.Green : Brushes.Red;
        }

        private void ClientTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateClientLengthText();
        }

        private void UpdateClientLengthText()
        {
            int bytes = Encoding.UTF8.GetByteCount(ClientTextBox.Text ?? string.Empty);
            ClientLengthText.Text = $"{bytes} / {_service.ClientMaxBytes} octets (UTF-8)";
            ClientLengthText.Foreground = bytes > _service.ClientMaxBytes ? Brushes.Red : Brushes.Gray;
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_service.IsLoaded)
            {
                ShowError("Aucune paire de clés", null);
                return;
            }

            if (!int.TryParse(ProductIdTextBox.Text, out int productId) || productId < 0)
            {
                ShowError("Identifiant produit invalide", null);
                return;
            }

            ulong features = 0;
            if (_service.Profile.HasFeatures && !FeatureMask.TryParseHex(FeaturesTextBox.Text, out features))
            {
                ShowError("Masque de modules (Features) invalide : attendu un hexadécimal 64 bits, ex. 0x000000000000000B", null);
                return;
            }

            string seatsText = SeatsTextBox.Text.Trim();
            ushort seats = 0;
            if (seatsText.Length > 0 && !ushort.TryParse(seatsText, out seats))
            {
                ShowError("Nombre de postes invalide : entier de 0 (non limité) à 65535", null);
                return;
            }

            string machineCode = null;
            if (BindMachineCheckBox.IsChecked == true && !MachineCode.TryNormalize(MachineCodeTextBox.Text, out machineCode))
            {
                ShowError("Code poste invalide : vérifiez la saisie (16 caractères, ex. WSZ6-G7W2-KQCT-CD3Q)", null);
                return;
            }

            var fields = new LicenceFields();
            foreach (var input in _fieldInputs)
            {
                if (!input.Key.TryApply(fields, input.Value(), out string fieldError))
                {
                    ShowError(fieldError, null);
                    return;
                }
            }

            DateTime? expiry = NeverExpiresCheckBox.IsChecked == true
                ? (DateTime?)null
                : ExpirationDatePicker.SelectedDate ?? DateTime.Today.AddYears(1);

            try
            {
                string key = _service.GenerateKey(ClientTextBox.Text, productId, expiry, features, seats, machineCode, fields);
                ResultTextBox.Text = key;
                CopyButton.IsEnabled = true;

                bool ok = _service.SelfCheck(key, out string details);
                SelfCheckText.Text = (ok ? "✓ " : "✗ ") + details;
                SelfCheckText.Foreground = ok ? Brushes.Green : Brushes.Red;

                string expiryLabel = expiry.HasValue ? expiry.Value.ToString("yyyy-MM-dd") : "jamais";
                HistoryListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  client=\"{ClientTextBox.Text}\"  produit={productId}{(_service.Profile.HasFeatures ? "  features=" + FeatureMask.FormatHex(features) : "")}  expire={expiryLabel}  postes={(seats == 0 ? "illimité" : seats.ToString())}  poste={machineCode ?? "tous"}  ->  {key}");
            }
            catch (Exception ex)
            {
                ShowError("Impossible de générer la clé", ex);
            }
        }

        // --- Onglet "Tester une clé" ---------------------------------------------------

        private void TestPasteButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText()) TestKeyTextBox.Text = Clipboard.GetText().Trim();
        }

        private void TestLastButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text)) TestKeyTextBox.Text = ResultTextBox.Text;
        }

        private void TestKeyButton_Click(object sender, RoutedEventArgs e)
        {
            TestDetailsTextBox.Text = string.Empty;
            if (!_service.IsLoaded)
            {
                SetTestStatus(false, "Aucune paire de clés chargée.");
                return;
            }

            KeyInspection r = _service.Inspect(TestKeyTextBox.Text, _catalog.Profiles, out string error);
            if (r == null)
            {
                SetTestStatus(false, error);
                return;
            }

            SetTestStatus(!r.Expired, r.Expired ? "Clé authentique mais EXPIRÉE" : "Clé valide");

            var sb = new StringBuilder();
            sb.AppendLine($"Produit    : {r.Profile.Name} ({r.ProductId})");
            sb.AppendLine($"Client     : {r.Client}");
            sb.AppendLine("Expiration : " + (r.Expiration.HasValue ? r.Expiration.Value.ToString("yyyy-MM-dd") : "jamais"));
            sb.AppendLine("Postes     : " + (r.Seats == 0 ? "non limité" : r.Seats.ToString()));
            sb.AppendLine("Lié au poste : " + (r.MachineCode ?? "non (tout poste)"));
            sb.AppendLine($"Licence n° : {r.LicenceId:X16}");
            AppendFields(sb, r);
            if (r.Features.HasValue)
            {
                ulong f = r.Features.Value;
                sb.AppendLine($"Features   : {FeatureMask.FormatHex(f)}  (décimal non signé : {FeatureMask.FormatDecimal(f)})");
                if (r.Profile.HasModules)
                {
                    sb.AppendLine();
                    if (f == FeatureMask.AllFeatures)
                        sb.AppendLine("Toutes options (inclut aussi les modules futurs).");
                    foreach (ProductModule m in r.Profile.Modules)
                        sb.AppendLine(((f & m.Mask) != 0 ? "  [x] " : "  [ ] ") + m.Name);
                    ulong unknown = f & ~r.Profile.Modules.Aggregate(0UL, (acc, m) => acc | m.Mask);
                    if (unknown != 0 && f != FeatureMask.AllFeatures)
                        sb.AppendLine($"Bits sans module connu : {FeatureMask.FormatHex(unknown)}");
                }
            }
            TestDetailsTextBox.Text = sb.ToString();
        }

        // Champs personnalises : nommes selon le profil, les numeros inconnus du profil en brut.
        private static void AppendFields(StringBuilder sb, KeyInspection r)
        {
            List<ProductField> known = r.Profile.Fields ?? new List<ProductField>();
            List<byte> unknown = r.Fields.Ids.Where(id => !known.Any(f => f.Id == id)).ToList();
            if (known.Count == 0 && unknown.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine("Informations produit :");
            foreach (ProductField f in known.OrderBy(f => f.Id))
                sb.AppendLine($"  {f.Name} : {f.Describe(r.Fields)}");
            foreach (byte id in unknown)
                sb.AppendLine($"  champ {id} (inconnu du profil) : {BitConverter.ToString(r.Fields.GetRaw(id))}");
        }

        private void SetTestStatus(bool ok, string text)
        {
            TestStatusText.Text = (ok ? "✓ " : "✗ ") + text;
            TestStatusText.Foreground = ok ? Brushes.Green : Brushes.Red;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text))
                Clipboard.SetText(ResultTextBox.Text);
        }

        private void CopyPublicKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsLoaded) Clipboard.SetText(_service.PublicKeyBase64);
        }

        private void ShowError(string title, Exception ex)
        {
            MessageBox.Show(this, ex == null ? title : title + " :\n" + ex.Message,
                title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
