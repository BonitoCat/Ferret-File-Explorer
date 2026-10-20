using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Models;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonFormDialog : Window
{
    private const int MaxRadioChoices = 6;

    private readonly List<(AddonField Field, Func<string> Read)> _readers = new();
    private readonly List<AddonField> _fields = new();
    private Control? _firstInput;

    public AddonFormDialog()
    {
        InitializeComponent();

        Opened += (_, _) => (_firstInput ?? ConfirmButton).Focus();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close(null);
            e.Handled = true;
        }, Tunnel);
    }

    public AddonFormDialog(string title, string message, IReadOnlyList<AddonField> fields, IReadOnlyDictionary<string, string>? values = null) : this()
    {
        Title = title;
        MessageText.Text = message;
        MessageText.IsVisible = message.Length > 0;
        EmptyText.IsVisible = fields.Count == 0;
        _fields.AddRange(fields);
        BuildFields(values);
    }

    public bool ShowReset
    {
        set => ResetButton.IsVisible = value;
    }

    public string ConfirmText
    {
        set => ConfirmButton.Content = value;
    }

    private void BuildFields(IReadOnlyDictionary<string, string>? values)
    {
        FieldsPanel.Children.Clear();
        _readers.Clear();
        _firstInput = null;

        foreach (AddonField field in _fields)
        {
            string value = values != null && values.TryGetValue(field.Id, out string? stored) ? field.Normalize(stored) : field.Default;
            FieldsPanel.Children.Add(CreateField(field, value));
        }
    }

    private Control CreateField(AddonField field, string value)
    {
        StackPanel container = new() { Spacing = 5 };

        if (field.Type == AddonFieldType.Bool)
        {
            CheckBox checkBox = new()
            {
                Content = field.Label,
                IsChecked = value == "true",
            };
            container.Children.Add(checkBox);
            _readers.Add((field, () => checkBox.IsChecked == true ? "true" : "false"));
            _firstInput ??= checkBox;
        }
        else
        {
            if (field.Label.Length > 0)
            {
                container.Children.Add(new TextBlock
                {
                    Text = field.Label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#BBBBBB")),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            container.Children.Add(CreateInput(field, value));
        }

        if (field.Description.Length > 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = field.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return container;
    }

    private Control CreateInput(AddonField field, string value)
    {
        switch (field.Type)
        {
            case AddonFieldType.Number:
                NumericUpDown number = new()
                {
                    Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null,
                    Minimum = field.Min is double min ? (decimal) min : decimal.MinValue,
                    Maximum = field.Max is double max ? (decimal) max : decimal.MaxValue,
                    FormatString = "0.########",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                _readers.Add((field, () => number.Value?.ToString(CultureInfo.InvariantCulture) ?? ""));
                _firstInput ??= number;
                return number;

            case AddonFieldType.Choice when field.Choices.Count <= MaxRadioChoices:
                StackPanel radios = new() { Spacing = 2 };
                string group = $"{field.Id}-{Guid.NewGuid():N}";
                foreach (string choice in field.Choices)
                {
                    radios.Children.Add(new RadioButton
                    {
                        Content = choice,
                        GroupName = group,
                        IsChecked = choice == value,
                    });
                }

                _readers.Add((field, () => radios.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? ""));
                _firstInput ??= radios.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) ?? radios.Children.OfType<RadioButton>().FirstOrDefault();
                return radios;

            case AddonFieldType.Choice:
                ComboBox combo = new()
                {
                    ItemsSource = field.Choices,
                    SelectedItem = value,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                _readers.Add((field, () => combo.SelectedItem as string ?? ""));
                _firstInput ??= combo;
                return combo;

            case AddonFieldType.File or AddonFieldType.Folder:
                return CreatePathInput(field, value);

            default:
                TextBox text = new()
                {
                    Text = value,
                    PasswordChar = field.Type == AddonFieldType.Password ? '•' : default,
                };
                _readers.Add((field, () => text.Text ?? ""));
                _firstInput ??= text;
                return text;
        }
    }

    private Control CreatePathInput(AddonField field, string value)
    {
        Grid grid = new() { ColumnDefinitions = new("*,Auto") };

        TextBox text = new() { Text = value };
        Grid.SetColumn(text, 0);

        Button browse = new()
        {
            Content = "…",
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new(12, 5),
            Margin = new(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(browse, 1);
        browse.Click += async (_, _) =>
        {
            string? picked = await PickPathAsync(field, text.Text ?? "");
            if (picked != null)
            {
                text.Text = picked;
            }
        };

        grid.Children.Add(text);
        grid.Children.Add(browse);
        _readers.Add((field, () => text.Text ?? ""));
        _firstInput ??= text;
        return grid;
    }

    private async Task<string?> PickPathAsync(AddonField field, string current)
    {
        string initialPath = Directory.Exists(current) ? current : Path.GetDirectoryName(current) ?? "";
        if (!Directory.Exists(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        FilePickerOptions options = new()
        {
            Mode = field.Type == AddonFieldType.Folder ? PickerMode.Folder : PickerMode.File,
            InitialPath = initialPath,
            Title = field.Label,
            ActionButtonText = LocalizationService.Get("Select"),
        };

        MainWindow picker = new(initialPath, options);
        FilePickerResult result = await picker.ShowPickerAsync(this);
        return result.IsSuccess ? result.SelectedPath : null;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Dictionary<string, string> values = new();
        foreach ((AddonField field, Func<string> read) in _readers)
        {
            values[field.Id] = field.Normalize(read());
        }

        Close(values);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        BuildFields(null);
    }
}
