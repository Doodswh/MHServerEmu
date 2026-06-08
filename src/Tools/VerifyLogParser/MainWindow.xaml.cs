using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace VerifyLogParser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Dictionary<uint, VerifyEntry> _verifyEntries;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoadVerifyLog(string filePath)
        {
            Dictionary<uint, VerifyEntry> verifyEntries = new();
            LogParserResult parserResult = LogParser.Parse(filePath, verifyEntries);

            if (parserResult != LogParserResult.Success)
            {
                MessageBox.Show($"Failed to load log file: {parserResult}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _verifyEntries = verifyEntries;
            Refresh();
        }

        private void Refresh()
        {
            SelectVerifyEntry(null);
            VerifyEntryListBox.Items.Clear();

            if (_verifyEntries == null)
                return;
            
            foreach (VerifyEntry verifyEntry in _verifyEntries.Values.OrderByDescending(verifyEntry => verifyEntry.Messages.Count))
            {
                string label = $"[{verifyEntry.Messages.Count}] {verifyEntry.File}:{verifyEntry.Line}";
                ListBoxItem item = new() { Content = label, Tag = verifyEntry };
                VerifyEntryListBox.Items.Add(item);
            }
        }

        private void SelectVerifyEntry(VerifyEntry verifyEntry)
        {
            if (verifyEntry != null)
            {
                VerifyFileTextBox.Text = verifyEntry.File;
                VerifyLineTextBox.Text = verifyEntry.Line.ToString();
                VerifyMemberTextBox.Text = verifyEntry.Member;
                VerifyCountTextBox.Text = verifyEntry.Messages.Count.ToString();

                VerifyMessageLogTextBox.Text = string.Join(null, verifyEntry.Messages);
                VerifyStackTraceTextBox.Text = verifyEntry.StackTrace;
            }
            else
            {
                VerifyFileTextBox.Text = string.Empty;
                VerifyLineTextBox.Text = string.Empty;
                VerifyMemberTextBox.Text = string.Empty;
                VerifyCountTextBox.Text = string.Empty;

                VerifyMessageLogTextBox.Text = string.Empty;
                VerifyStackTraceTextBox.Text = string.Empty;
            }
        }

        #region Event Handlers

        private void FileOpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new();
            dialog.Filter = "Log files (.log)|*.log";
            bool? result = dialog.ShowDialog();
            if (result != true)
                return;

            LoadVerifyLog(dialog.FileName);
        }

        private void VerifyEntryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBoxItem selectedItem = VerifyEntryListBox.SelectedItem as ListBoxItem;
            if (selectedItem == null)
                return;

            SelectVerifyEntry(selectedItem.Tag as VerifyEntry);
        }

        #endregion
    }
}