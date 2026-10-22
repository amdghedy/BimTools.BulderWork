using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitSleeveAddin.UI
{
    public class CreateSleevesWindow : Window
    {
        public bool Confirmed { get; private set; }
        public double ClearanceMm { get; private set; } = 2.0;
        public bool ProcessLinkedModels { get; private set; } = true;
        public bool ProcessWalls { get; private set; } = true;
        public bool ProcessFloors { get; private set; } = true;

        private TextBox _clearanceBox;
        private CheckBox _wallsCheck;
        private CheckBox _floorsCheck;
        private CheckBox _linksCheck;

        public CreateSleevesWindow()
        {
            Title = "Create Sleeves";
            Width = 440;
            MinWidth = 360;
            Height = 440;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(new TextBlock
            {
                Text = "Create Sleeves",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30))
            });
            header.Children.Add(new TextBlock
            {
                Text = "Places sleeves where MEP elements pass through walls and/or floors.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Options card
            var options = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };
            var optStack = new StackPanel();

            // Hosts
            optStack.Children.Add(new TextBlock
            {
                Text = "Hosts",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _wallsCheck = new CheckBox
            {
                Content = "Walls",
                IsChecked = true,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _floorsCheck = new CheckBox
            {
                Content = "Floors / Slabs",
                IsChecked = true,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            };
            optStack.Children.Add(_wallsCheck);
            optStack.Children.Add(_floorsCheck);

            // Clearance – free text
            optStack.Children.Add(new TextBlock
            {
                Text = "Clearance / Tolerance (mm)",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 6)
            });

            var clearanceRow = new StackPanel { Orientation = Orientation.Horizontal };
            _clearanceBox = new TextBox
            {
                Text = "2",
                Width = 100,
                Height = 28,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2)
            };
            clearanceRow.Children.Add(_clearanceBox);
            clearanceRow.Children.Add(new TextBlock
            {
                Text = "  mm   (extra around MEP size)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            });
            optStack.Children.Add(clearanceRow);

            // Quick presets
            var presets = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            presets.Children.Add(MakePresetButton("2", _clearanceBox));
            presets.Children.Add(MakePresetButton("5", _clearanceBox));
            presets.Children.Add(MakePresetButton("10", _clearanceBox));
            presets.Children.Add(MakePresetButton("15", _clearanceBox));
            presets.Children.Add(MakePresetButton("20", _clearanceBox));
            optStack.Children.Add(presets);

            optStack.Children.Add(new TextBlock
            {
                Text = "Type any value, or click a preset.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            // Links
            _linksCheck = new CheckBox
            {
                Content = "Process linked models",
                IsChecked = true,
                Margin = new Thickness(0, 14, 0, 0),
                FontSize = 13
            };
            optStack.Children.Add(_linksCheck);

            optStack.Children.Add(new TextBlock
            {
                Text = "Families required:\n  • WallOpening_Round\n  • WallOpening_Rectangular",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                Margin = new Thickness(0, 14, 0, 0)
            });

            options.Child = optStack;
            Grid.SetRow(options, 1);
            root.Children.Add(options);

            // Buttons
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1)
            };
            cancelBtn.Click += (s, e) => { Confirmed = false; Close(); };

            var okBtn = new Button
            {
                Content = "Create Sleeves",
                Width = 130,
                Height = 32,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okBtn.Click += OnOk;

            buttons.Children.Add(cancelBtn);
            buttons.Children.Add(okBtn);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }

        private static Button MakePresetButton(string mm, TextBox target)
        {
            var b = new Button
            {
                Content = mm + " mm",
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11
            };
            b.Click += (s, e) => { target.Text = mm; };
            return b;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            ProcessWalls = _wallsCheck.IsChecked == true;
            ProcessFloors = _floorsCheck.IsChecked == true;

            if (!ProcessWalls && !ProcessFloors)
            {
                MessageBox.Show("Select at least one host: Walls or Floors/Slabs.", "Create Sleeves",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string raw = (_clearanceBox.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double mm)
                || mm < 0 || mm > 500)
            {
                MessageBox.Show(
                    "Enter a valid clearance in millimetres (0 – 500).\nExample: 2 or 12.5",
                    "Invalid clearance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _clearanceBox.Focus();
                _clearanceBox.SelectAll();
                return;
            }

            ClearanceMm = mm;
            ProcessLinkedModels = _linksCheck.IsChecked == true;
            Confirmed = true;
            Close();
        }
    }
}
