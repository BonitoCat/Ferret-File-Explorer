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
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonFormDialog : Window
{
    private const int MaxRadioChoices = 6;

    private readonly List<(AddonField Field, Func<string> Read)> _readers = new();
    private readonly Dictionary<string, Action<string>> _writers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private (string Id, IReadOnlyList<string> Range)? _initialSelection;
    private readonly List<AddonField> _fields = new();
    private Control? _firstInput;
    private bool _isWriting;

    public event Action<string>? ValueChanged;

    public AddonFormDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (_initialSelection is { } selection && TrySelectText(selection.Id, selection.Range))
            {
                return;
            }

            (_firstInput ?? ConfirmButton).Focus();
        };

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

    public void SelectOnOpen(string id, IReadOnlyList<string> range)
    {
        _initialSelection = (id, range);
    }

    public static bool IsValidSelection(IReadOnlyList<string> range)
    {
        return TryGetSelection(range, "", out _, out _);
    }

    public bool TrySelectText(string id, IReadOnlyList<string> range)
    {
        if (!_textBoxes.TryGetValue(id, out TextBox? textBox) || !TryGetSelection(range, textBox.Text ?? "", out int start, out int end))
        {
            return false;
        }

        textBox.Focus();
        textBox.CaretIndex = end;
        textBox.SelectionStart = start;
        textBox.SelectionEnd = end;
        return true;
    }

    private static bool TryGetSelection(IReadOnlyList<string> range, string text, out int start, out int end)
    {
        start = 0;
        end = text.Length;
        string first = range.Count > 0 ? range[0].ToLowerInvariant() : "all";
        if (first is "" or "all")
        {
            return range.Count <= 1;
        }

        if (first == "name")
        {
            end = FileUtils.GetNameLength(text);
            return range.Count == 1;
        }

        if (range.Count > 2 || !int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start))
        {
            return false;
        }

        end = start;
        if (range.Count == 2 && !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out end))
        {
            return false;
        }

        start = Math.Min(start, text.Length);
        end = Math.Clamp(end, start, text.Length);
        return true;
    }

    public Dictionary<string, string> GetValues()
    {
        Dictionary<string, string> values = new();
        foreach ((AddonField field, Func<string> read) in _readers)
        {
            values[field.Id] = field.Normalize(read());
        }

        return values;
    }

    public bool TrySetValue(string id, string value)
    {
        AddonField? field = _fields.FirstOrDefault(f => f.Id == id);
        if (field == null || !_writers.TryGetValue(id, out Action<string>? write))
        {
            return false;
        }

        string normalized = field.Normalize(value);
        _isWriting = true;
        try
        {
            write(normalized);
        }
        finally
        {
            _isWriting = false;
        }

        _knownValues[id] = ReadValue(field) ?? normalized;
        return true;
    }

    private void NotifyChanged(AddonField field)
    {
        string? value = ReadValue(field);
        if (_isWriting || value == null || _knownValues.GetValueOrDefault(field.Id) == value)
        {
            return;
        }

        _knownValues[field.Id] = value;
        ValueChanged?.Invoke(field.Id);
    }

    private string? ReadValue(AddonField field)
    {
        Func<string>? read = _readers.FirstOrDefault(r => r.Field == field).Read;
        return read == null ? null : field.Normalize(read());
    }

    private void BuildFields(IReadOnlyDictionary<string, string>? values)
    {
        FieldsPanel.Children.Clear();
        _readers.Clear();
        _writers.Clear();
        _knownValues.Clear();
        _textBoxes.Clear();
        _firstInput = null;

        foreach (AddonField field in _fields)
        {
            string value = values != null && values.TryGetValue(field.Id, out string? stored) ? field.Normalize(stored) : field.Default;
            FieldsPanel.Children.Add(CreateField(field, value));
            _knownValues[field.Id] = ReadValue(field) ?? value;
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
            _writers[field.Id] = text => checkBox.IsChecked = text == "true";
            checkBox.IsCheckedChanged += (_, _) => NotifyChanged(field);
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
                _writers[field.Id] = text => number.Value = decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal written) ? written : null;
                number.ValueChanged += (_, _) => NotifyChanged(field);
                _firstInput ??= number;
                return number;

            case AddonFieldType.Choice when field.Choices.Count <= MaxRadioChoices:
                StackPanel radios = new() { Spacing = 2 };
                string group = $"{field.Id}-{Guid.NewGuid():N}";
                string selected = value;
                foreach (string choice in field.Choices)
                {
                    RadioButton radio = new()
                    {
                        Content = choice,
                        GroupName = group,
                        IsChecked = choice == value,
                    };
                    radio.IsCheckedChanged += (_, _) =>
                    {
                        if (radio.IsChecked != true)
                        {
                            return;
                        }

                        selected = choice;
                        NotifyChanged(field);
                    };
                    radios.Children.Add(radio);
                }

                radios.KeyDown += OnRadioGroupKeyDown;
                _readers.Add((field, () => selected));
                _writers[field.Id] = text =>
                {
                    selected = text;
                    foreach (RadioButton radio in radios.Children.OfType<RadioButton>())
                    {
                        radio.IsChecked = radio.Content as string == text;
                    }
                };
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
                _writers[field.Id] = text => combo.SelectedItem = text;
                combo.SelectionChanged += (_, _) => NotifyChanged(field);
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
                _writers[field.Id] = written => SetTextKeepingCaret(text, written);
                _textBoxes[field.Id] = text;
                text.TextChanged += (_, _) => NotifyChanged(field);
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
        _writers[field.Id] = written => SetTextKeepingCaret(text, written);
        _textBoxes[field.Id] = text;
        text.TextChanged += (_, _) => NotifyChanged(field);
        _firstInput ??= text;
        return grid;
    }

    private static void OnRadioGroupKeyDown(object? sender, KeyEventArgs e)
    {
        int step = e.Key switch
        {
            Key.Up or Key.Left => -1,
            Key.Down or Key.Right => 1,
            _ => 0,
        };
        if (step == 0 || sender is not StackPanel panel || e.Source is not RadioButton current)
        {
            return;
        }

        List<RadioButton> radios = panel.Children.OfType<RadioButton>().ToList();
        int index = Math.Clamp(radios.IndexOf(current) + step, 0, radios.Count - 1);
        radios[index].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private static void SetTextKeepingCaret(TextBox textBox, string text)
    {
        if (textBox.Text == text)
        {
            return;
        }

        int caret = textBox.CaretIndex;
        textBox.Text = text;
        textBox.CaretIndex = Math.Min(caret, text.Length);
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
        Close(GetValues());
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
