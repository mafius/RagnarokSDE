﻿using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ErrorManager;
using GRF.IO;
using GRF.Threading;
using SDE.ApplicationConfiguration;
using SDE.Others;
using SDE.Tools.DatabaseEditor.WPF;
using TokeiLibrary;
using TokeiLibrary.WPF;
using TokeiLibrary.WPF.Styles;
using Utilities;
using Utilities.Services;

namespace SDE.Tools.DatabaseEditor {
	public partial class SDEditor : TkWindow {
		private readonly MetaGrfHolder _metaGrf = new MetaGrfHolder();
		private bool _delayedReloadDatabase;
		private TextViewItem _tviItemDb;

		public bool ShouldCancelDbReload() {
			if (_clientDatabase.IsModified) {
				MessageBoxResult result = WindowProvider.ShowDialog("The database has been modified, would you like to save it first?", "Modified database", MessageBoxButton.YesNoCancel);

				if (result == MessageBoxResult.Yes || result == MessageBoxResult.Cancel) {
					return true;
				}
			}

			_clientDatabase.IsModified = false;
			return false;
		}

		public void ReloadSettings(string fileNameSettings = null) {
			try {
				if (ShouldCancelDbReload()) return;

				if (fileNameSettings != null) {
					if (fileNameSettings == SDEConfiguration.DefaultFileName) {
						GrfPath.Delete(SDEConfiguration.DefaultFileName);
					}

					SDEConfiguration.ConfigAsker = new ConfigAsker(fileNameSettings);
					_recentFilesManager.AddRecentFile(fileNameSettings);
				}

				Title = "Server database editor - " + Methods.CutFileName(SDEConfiguration.ConfigAsker.ConfigFile);

				_loadDatabaseFiles();
				_metaGrfViewer.LoadResourcesInfo();
				_asyncOperation.SetAndRunOperation(new GrfThread(_updateMetaGrf, this, 200, null, false, true));
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
			}
		}
		public bool ReloadDatabase() {
			try {
				if (ShouldCancelDbReload()) return false;

				if (_asyncOperation.IsRunning) {
					_reloadDatabase(true);
				}
				else {
					_asyncOperation.SetAndRunOperation(new GrfThread(() => _reloadDatabase(false), this, 200, null, false, true));
				}
				return true;
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
				return false;
			}
		}

		private void _loadSettingsTab() {
			_metaGrfViewer.SaveResourceMethod = delegate(string resources) {
				SDEConfiguration.SDEditorResources = resources;
				ReloadSettings();
			};

			SDEAppConfiguration.Bind(_cbEnableBackups, () => SDEAppConfiguration.BackupsManagerState, v => SDEAppConfiguration.BackupsManagerState = v);

			_metaGrfViewer.LoadResourceMethod = () => Methods.StringToList(SDEConfiguration.SDEditorResources);
			_metaGrfViewer.LoadResourcesInfo();

			_loadEncoding();
			_loadTxtFiles();
			_loadDatabaseFiles();
		}

		private void _loadEncoding() {
			_comboBoxEncoding.Items.Add("Default (codeage 1252 - Western European [Windows])");
			_comboBoxEncoding.Items.Add("Korean (codepage 949 - ANSI/OEM Korean [Unified Hangul Code])");
			_comboBoxEncoding.Items.Add("Other...");

			switch (SDEAppConfiguration.EncodingCodepage) {
				case 1252:
					_comboBoxEncoding.SelectedIndex = 0;
					break;
				case 949:
					_comboBoxEncoding.SelectedIndex = 1;
					break;
				default:
					_comboBoxEncoding.Items[2] = SDEAppConfiguration.EncodingCodepage + "...";
					_comboBoxEncoding.SelectedIndex = 2;
					break;
			}

			_comboBoxEncoding.SelectionChanged += _comboBoxEncoding_SelectionChanged;
		}

		public bool SetEncoding(int encoding) {
			try {
				EncodingService.SetDisplayEncoding(encoding);
				SDEAppConfiguration.EncodingCodepage = encoding;
				return true;
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err.Message, ErrorLevel.Critical);
				return false;
			}
		}

		private void _comboBoxEncoding_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			object oldSelected = null;
			bool cancel = false;

			if (e.RemovedItems.Count > 0)
				oldSelected = e.RemovedItems[0];

			switch (_comboBoxEncoding.SelectedIndex) {
				case 0:
					if (!SetEncoding(1252)) cancel = true;
					break;
				case 1:
					if (!SetEncoding(949)) cancel = true;
					break;
				case 2:
					InputDialog dialog = WindowProvider.ShowWindow<InputDialog>(new InputDialog("Using an unsupported encoding may cause unexpected results, make a copy of your GRF file before saving!\nEnter the codepage number for the encoding :",
																								"Encoding", _comboBoxEncoding.Items[2].ToString().IndexOf(' ') > 0 ? _comboBoxEncoding.Items[2].ToString().Substring(0, _comboBoxEncoding.Items[2].ToString().IndexOf(' ')) : EncodingService.DisplayEncoding.CodePage.ToString(CultureInfo.InvariantCulture)), this);

					bool pageExists;

					if (dialog.Result == MessageBoxResult.OK) {
						pageExists = EncodingService.EncodingExists(dialog.Input);

						if (pageExists) {
							_comboBoxEncoding.SelectionChanged -= _comboBoxEncoding_SelectionChanged;
							_comboBoxEncoding.Items[2] = dialog.Input + "...";
							_comboBoxEncoding.SelectedIndex = 2;
							_comboBoxEncoding.SelectionChanged += _comboBoxEncoding_SelectionChanged;
							if (!SetEncoding(Int32.Parse(dialog.Input))) cancel = true;
						}
						else {
							cancel = true;
						}
					}
					else {
						cancel = true;
					}

					break;
				default:
					if (!SetEncoding(1252)) cancel = true;
					break;
			}

			if (cancel) {
				_comboBoxEncoding.SelectionChanged -= _comboBoxEncoding_SelectionChanged;

				if (oldSelected != null) {
					_comboBoxEncoding.SelectedItem = oldSelected;
				}

				_comboBoxEncoding.SelectionChanged += _comboBoxEncoding_SelectionChanged;
			}
			else {
				_delayedReloadDatabase = true;
			}
		}

		private void _loadTxtFiles() {
			_tviItemDb = new TextViewItem(null, SDEConfiguration.DatabasePath, _metaGrf, SdeFiles.ServerDbPath) { Description = "Server DB path" };
			_tviItemDb.Browser.BrowseMode = PathBrowser.BrowseModeType.Folder;
			_tviItemDb.HorizontalAlignment = HorizontalAlignment.Left;
			_tviItemDb.Margin = new Thickness(4, 0, 0, 0);
			
			_gridTextFilesSettings.Children.Add(_tviItemDb);
			_gridTextFilesSettings.SizeChanged += new SizeChangedEventHandler(_grid_SizeChanged);
		}

		private void _grid_SizeChanged(object sender, SizeChangedEventArgs e) {
			double width = _gridTextFilesSettings.ColumnDefinitions[0].ActualWidth - 11;
			_tviItemDb._grid.Width = width < 0 ? 0 : width;
		}

		private void _loadDatabaseFiles() {
			try {
				_tviItemDb.TextBoxItem.TextChanged -= _tviItemDb_TextChanged;
				_tviItemDb.TextBoxItem.Text = SDEConfiguration.DatabasePath;
				_tviItemDb.TextBoxItem.TextChanged += _tviItemDb_TextChanged;
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
			}
		}

		private void _tviItemDb_TextChanged(object sender, TextChangedEventArgs e) {
			try {
				_delayedReloadDatabase = true;

				TextViewItem item = WpfUtilities.FindParentControl<TextViewItem>(sender as TextBox);

				if (item != null) {
					SDEConfiguration.DatabasePath = _tviItemDb.TextBoxItem.Text;
					item.CheckValid();
				}
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
			}
		}
		
		private void _updateMetaGrf() {
			try {
				_mainTabControl.Dispatch(p => p.IsEnabled = false);
				Progress = -1;
				_metaGrf.Update(_metaGrfViewer.Paths);
				_delayedReloadDatabase = true;

				_tviItemDb.Dispatcher.Invoke(new Action(delegate {
					_tviItemDb.CheckValid();
				}));

				_mainTabControl.Dispatcher.Invoke(new Action(delegate {
					try {
						if (!WpfUtilities.IsTab(_mainTabControl.SelectedItem as TabItem, "Settings")) {
							ReloadDatabase();
						}
					}
					catch (Exception err) {
						ErrorHandler.HandleException(err);
					}
				}));
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
			}
			finally {
				Progress = 100;
				_mainTabControl.Dispatch(p => p.IsEnabled = true);
			}
		}
		private void _reloadDatabase(bool alreadyAsync) {
			try {
				if (!alreadyAsync) {
					Progress = -1;
					_mainTabControl.Dispatch(p => p.IsEnabled = false);
				}

				_debugList.Dispatch(p => _debugItems.Clear());
				_clientDatabase.Reload(this);
				_clientDatabase.ClearCommands();
			}
			catch (Exception err) {
				ErrorHandler.HandleException(err);
			}
			finally {
				_delayedReloadDatabase = false;

				if (!alreadyAsync) {
					Progress = 100;
					_mainTabControl.Dispatch(p => p.IsEnabled = true);
				}

				_debugList.Dispatch(delegate {
					if (_debugItems.Count > 0) {
						_debugList.ScrollIntoView(_debugItems.Last());
						WpfUtilities.GetTab(_mainTabControl, "Error console").IsSelected = true;
					}
				});
			}
		}
	}
}
