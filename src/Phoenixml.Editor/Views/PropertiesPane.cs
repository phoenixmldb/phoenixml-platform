using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Editor.Views;

/// <summary>
/// A small panel below the tree that shows the selected element's <c>LocalName</c>
/// in an editable <see cref="TextBox"/>. Enter commits a <see cref="RenameElementCommand"/>
/// through the document's <see cref="Phoenixml.Platform.Abstractions.Documents.IUndoStack"/>;
/// Escape reverts the textbox; LostFocus auto-commits the change.
/// </summary>
internal sealed class PropertiesPane : UserControl
{
    private readonly TextBox _localNameBox;
    private readonly TextBlock _hint;

    private XmlDocument? _document;
    private XmlTreeNode? _selectedNode;

    // Snapshot of the committed local name — used to detect real changes on commit.
    private string _committedName = string.Empty;

    /// <summary>Initializes a new <see cref="PropertiesPane"/> and builds its child controls.</summary>
    public PropertiesPane()
    {
        var secondaryBrush = Application.Current?.TryFindResource("Phoenixml.Color.Content.Secondary", out var r) == true
            ? r as IBrush
            : null;

        var label = new TextBlock
        {
            Text = "Element name",
            FontSize = 11,
            Foreground = secondaryBrush,
            Margin = new Thickness(8, 8, 8, 2),
        };

        _localNameBox = new TextBox
        {
            Watermark = "(select an element)",
            IsEnabled = false,
            Margin = new Thickness(8, 0, 8, 4),
        };

        _hint = new TextBlock
        {
            Text = "Select an element...",
            FontSize = 11,
            Foreground = secondaryBrush,
            Margin = new Thickness(8, 0, 8, 8),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                label,
                _localNameBox,
                _hint,
            },
        };

        Content = panel;

        _localNameBox.KeyDown += OnLocalNameBoxKeyDown;
        _localNameBox.LostFocus += OnLocalNameBoxLostFocus;
    }

    /// <summary>
    /// Updates the pane to reflect a new tree selection. Populates the textbox when
    /// <paramref name="selected"/> wraps an <see cref="XdmElement"/>; otherwise clears
    /// and disables the textbox.
    /// </summary>
    /// <param name="document">
    /// The active <see cref="XmlDocument"/>, or <see langword="null"/> when no document
    /// is open.
    /// </param>
    /// <param name="selected">
    /// The newly selected tree node, or <see langword="null"/> when the selection is cleared.
    /// </param>
    public void OnSelectionChanged(XmlDocument? document, XmlTreeNode? selected)
    {
        _document = document;
        _selectedNode = selected;

        if (selected?.XdmNode is XdmElement el)
        {
            _committedName = el.LocalName;
            _localNameBox.Text = el.LocalName;
            _localNameBox.IsEnabled = true;
            _hint.Text = "Enter to commit; Escape to cancel.";
        }
        else
        {
            _committedName = string.Empty;
            _localNameBox.Text = string.Empty;
            _localNameBox.IsEnabled = false;
            _hint.Text = "Select an element...";
        }
    }

    private void OnLocalNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void OnLocalNameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitRename();
    }

    private void CommitRename()
    {
        if (_document is null || _selectedNode is null)
            return;

        if (_selectedNode.XdmNode is not XdmElement currentElement)
            return;

        var newName = _localNameBox.Text ?? string.Empty;

        // Only issue a command if the name actually changed.
        if (newName == _committedName || string.IsNullOrWhiteSpace(newName))
        {
            // Revert the textbox to the last committed value if the user typed whitespace.
            if (string.IsNullOrWhiteSpace(newName))
                _localNameBox.Text = _committedName;
            return;
        }

        var command = new RenameElementCommand(_document, currentElement.Id, newName);
        _document.Undo.Execute(command);

        // Update the committed snapshot so a second LostFocus doesn't re-fire.
        _committedName = newName;
    }

    private void CancelRename()
    {
        _localNameBox.Text = _committedName;
    }
}
